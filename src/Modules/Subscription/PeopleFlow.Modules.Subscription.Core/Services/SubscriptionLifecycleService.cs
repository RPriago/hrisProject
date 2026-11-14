using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Entities;
using PeopleFlow.Modules.Subscription.Core.Persistence;
using PeopleFlow.Persistence.Context;

namespace PeopleFlow.Modules.Subscription.Core.Services;

public sealed class SubscriptionLifecycleService : ISubscriptionLifecycleService
{
    private readonly SubscriptionDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IActiveApiClientCounter? _activeApiClientCounter;
    private readonly PeopleFlowDbContext? _infraDb;

    public SubscriptionLifecycleService(
        SubscriptionDbContext dbContext,
        IClock clock,
        IActiveApiClientCounter? activeApiClientCounter = null,
        PeopleFlowDbContext? infraDb = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _activeApiClientCounter = activeApiClientCounter;
        _infraDb = infraDb;
    }

    public async Task<Result> ValidateActivePlanAsync(string planCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planCode) || planCode.Trim().Length > 48)
        {
            return Result.Failure(Error.Validation("INVALID_PLAN_CODE", "A valid plan code (1..48 characters) is required."));
        }

        var normalizedPlanCode = planCode.Trim();
        var exists = await _dbContext.SubscriptionPlans.AsNoTracking()
            .AnyAsync(p => p.Code == normalizedPlanCode && p.IsActive, cancellationToken);
        return exists
            ? Result.Success()
            : Result.Failure(Error.NotFound("PLAN_NOT_FOUND", $"Subscription plan '{normalizedPlanCode}' not found."));
    }

    public async Task CompensateInitialProvisionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return;
        }

        await _dbContext.TenantSubscriptions
            .Where(s => s.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<Result<TenantSubscriptionSnapshot>> ProvisionInitialAsync(
        Guid tenantId,
        string planCode,
        bool startAsTrial,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Validation("INVALID_TENANT_ID", "Tenant ID is required."));
        }

        if (string.IsNullOrWhiteSpace(planCode) || planCode.Trim().Length > 48)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Validation("INVALID_PLAN_CODE", "A valid plan code (1..48 characters) is required."));
        }

        var normalizedPlanCode = planCode.Trim();
        var alreadyExists = await _dbContext.TenantSubscriptions.AsNoTracking()
            .AnyAsync(s => s.TenantId == tenantId, cancellationToken);
        if (alreadyExists)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict("SUBSCRIPTION_ALREADY_EXISTS", "Tenant already has a subscription."));
        }

        var plan = await _dbContext.SubscriptionPlans.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Code == normalizedPlanCode && p.IsActive, cancellationToken);
        if (plan == null)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.NotFound("PLAN_NOT_FOUND", $"Subscription plan '{normalizedPlanCode}' not found."));
        }

        var now = _clock.UtcNow;
        var entity = new TenantSubscription(Guid.CreateVersion7())
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = startAsTrial ? SubscriptionStatusConstants.Trial : SubscriptionStatusConstants.Active,
            CurrentPeriodStartUtc = now,
            CurrentPeriodEndUtc = startAsTrial ? now.AddDays(14) : now.AddMonths(1),
            TrialEndsAtUtc = startAsTrial ? now.AddDays(14) : null
        };

        _dbContext.TenantSubscriptions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(MapSnapshot(entity, plan, now));
    }

    public async Task<Result<TenantSubscriptionSnapshot>> ChangePlanAsync(
        Guid tenantId,
        string targetPlanCode,
        int currentActiveEmployees,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Validation("INVALID_TENANT_ID", "Tenant ID is required."));
        }

        if (string.IsNullOrWhiteSpace(targetPlanCode) || targetPlanCode.Trim().Length > 48)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Validation("INVALID_PLAN_CODE", "A valid target plan code (1..48 characters) is required."));
        }

        if (currentActiveEmployees < 0)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Validation("INVALID_ACTIVE_EMPLOYEES", "Active employee count must not be negative."));
        }

        var normalizedTargetCode = targetPlanCode.Trim();
        var ownTx = _dbContext.Database.CurrentTransaction is null;
        await using var tx = ownTx ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

        if (_dbContext.Database.IsNpgsql())
        {
            // Serialize concurrent plan changes per tenant subscription row.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM subscription_tenant_subscriptions WHERE tenant_id = {tenantId} FOR UPDATE",
                cancellationToken);
        }

        var sub = await _dbContext.TenantSubscriptions.SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (sub == null)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.NotFound("SUBSCRIPTION_NOT_FOUND", "Tenant subscription not found."));
        }

        if (string.Equals(sub.Status, SubscriptionStatusConstants.Suspended, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sub.Status, SubscriptionStatusConstants.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict(
                "INVALID_STATE_TRANSITION",
                $"Cannot change plan while subscription is '{sub.Status}'."));
        }

        if (string.Equals(sub.Status, SubscriptionStatusConstants.PastDue, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict(
                "PENDING_TRANSACTIONS_EXIST",
                "Cannot change plan while subscription has past-due billing."));
        }

        var targetPlan = await _dbContext.SubscriptionPlans.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Code == normalizedTargetCode && p.IsActive, cancellationToken);
        if (targetPlan == null)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.NotFound("PLAN_NOT_FOUND", $"Target plan '{normalizedTargetCode}' not found."));
        }

        var hasOpenInvoices = await _dbContext.SubscriptionInvoices.AsNoTracking()
            .AnyAsync(
                i => i.TenantId == tenantId &&
                     (i.Status == "Open" || i.Status == "Pending" || i.Status == "Unpaid"),
                cancellationToken);
        if (hasOpenInvoices)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict(
                "PENDING_TRANSACTIONS_EXIST",
                "Cannot change plan while unpaid or pending invoices exist."));
        }

        long usedStorageBytes = 0;
        if (_infraDb != null)
        {
            using var _ = _infraDb.BeginOperationTenantScope(tenantId);

            var hasActiveJobs = await _infraDb.BackgroundJobRuns.AsNoTracking()
                .AnyAsync(
                    j => j.TenantId == tenantId &&
                         (j.Status == "Queued" || j.Status == "Validating" || j.Status == "Processing" ||
                          j.Status == "Running" || j.Status == "Pending"),
                    cancellationToken);
            if (hasActiveJobs)
            {
                return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict(
                    "PENDING_TRANSACTIONS_EXIST",
                    "Cannot change plan while background jobs are running or queued."));
            }

            usedStorageBytes = await _infraDb.StoredFiles.AsNoTracking()
                .Where(f => f.TenantId == tenantId &&
                            f.ScanStatus != "RejectedInfected" &&
                            f.ScanStatus != "RejectedInvalidType" &&
                            f.ScanStatus != "Deleted")
                .SumAsync(f => (long?)f.SizeBytes, cancellationToken) ?? 0L;
        }

        var effectiveMax = sub.OverrideMaxEmployees ?? targetPlan.MaxActiveEmployees;
        if (currentActiveEmployees > effectiveMax)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict(
                "DOWNGRADE_HEADCOUNT_EXCEEDED",
                $"Active employee count ({currentActiveEmployees}) exceeds target plan limit ({effectiveMax})."));
        }

        var effectiveStorageMax = sub.OverrideMaxStorageBytes ?? targetPlan.MaxStorageBytes;
        if (usedStorageBytes > effectiveStorageMax)
        {
            return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict(
                "DOWNGRADE_STORAGE_EXCEEDED",
                $"Used storage ({usedStorageBytes} bytes) exceeds target plan limit ({effectiveStorageMax} bytes)."));
        }

        if (_activeApiClientCounter != null)
        {
            var activeApiClients = await _activeApiClientCounter.CountActiveApiClientsAsync(tenantId, cancellationToken);
            if (activeApiClients > targetPlan.MaxApiClients)
            {
                return Result.Failure<TenantSubscriptionSnapshot>(Error.Conflict(
                    "DOWNGRADE_API_CLIENT_EXCEEDED",
                    $"Active API client count ({activeApiClients}) exceeds target plan limit ({targetPlan.MaxApiClients})."));
            }
        }

        sub.PlanId = targetPlan.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }
        return Result.Success(MapSnapshot(sub, targetPlan, _clock.UtcNow));
    }

    public async Task<Result> UpdateOverridesAsync(
        Guid tenantId,
        int? overrideMaxEmployees,
        long? overrideMaxStorageBytes,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("INVALID_TENANT_ID", "Tenant ID is required."));
        }

        if (overrideMaxEmployees is < 0 || overrideMaxStorageBytes is < 0)
        {
            return Result.Failure(Error.Validation("INVALID_OVERRIDE_LIMIT", "Override limits must not be negative."));
        }

        var sub = await _dbContext.TenantSubscriptions.SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (sub == null)
        {
            return Result.Failure(Error.NotFound("SUBSCRIPTION_NOT_FOUND", "Tenant subscription not found."));
        }

        sub.OverrideMaxEmployees = overrideMaxEmployees;
        sub.OverrideMaxStorageBytes = overrideMaxStorageBytes;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<SubscriptionInvoiceDto>> RecordManualPaymentAsync(
        Guid tenantId,
        decimal amount,
        string currency,
        string paymentReference,
        Guid adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || adminUserId == Guid.Empty)
        {
            return Result.Failure<SubscriptionInvoiceDto>(Error.Validation("INVALID_PAYMENT_ACTOR", "Tenant ID and admin user ID are required."));
        }

        if (amount <= 0m)
        {
            return Result.Failure<SubscriptionInvoiceDto>(Error.Validation("INVALID_PAYMENT_AMOUNT", "Manual payment amount must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(currency) ||
            currency.Trim().Length != 3 ||
            !currency.Trim().All(char.IsAsciiLetter))
        {
            return Result.Failure<SubscriptionInvoiceDto>(Error.Validation("INVALID_CURRENCY", "Currency must be a 3-letter ISO code."));
        }

        if (string.IsNullOrWhiteSpace(paymentReference) || string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<SubscriptionInvoiceDto>(Error.Validation("PAYMENT_REFERENCE_REQUIRED", "Payment reference and audit reason are required."));
        }

        var trimmedReference = paymentReference.Trim();
        var trimmedReason = reason.Trim();
        if (trimmedReference.Length > 160 || trimmedReason.Length > 500)
        {
            return Result.Failure<SubscriptionInvoiceDto>(Error.Validation("PAYMENT_INPUT_TOO_LONG", "Payment reference (<=160) or reason (<=500) exceeds maximum length."));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var ownTx = _dbContext.Database.CurrentTransaction is null;
        await using var tx = ownTx ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

        var sub = await _dbContext.TenantSubscriptions.SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (sub == null)
        {
            return Result.Failure<SubscriptionInvoiceDto>(Error.NotFound("SUBSCRIPTION_NOT_FOUND", "Tenant subscription not found."));
        }

        if (!CanTransition(sub.Status, SubscriptionStatusConstants.Active) &&
            !string.Equals(sub.Status, SubscriptionStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<SubscriptionInvoiceDto>(Error.Conflict("INVALID_STATE_TRANSITION", $"Cannot activate subscription from '{sub.Status}'."));
        }

        var now = _clock.UtcNow;
        var invoice = new SubscriptionInvoice(Guid.CreateVersion7())
        {
            TenantId = tenantId,
            InvoiceNumber = $"INV-{now:yyyyMMdd}-{Guid.CreateVersion7().ToString("N")[..8].ToUpperInvariant()}",
            Amount = amount,
            Currency = normalizedCurrency,
            Status = "Paid",
            PaymentChannel = "ManualAdmin",
            PaymentReference = trimmedReference,
            PaidAtUtc = now,
            RecordedByAdminId = adminUserId,
            Notes = trimmedReason
        };

        sub.Status = SubscriptionStatusConstants.Active;
        sub.GracePeriodEndsAtUtc = null;
        sub.ReadOnlySinceUtc = null;
        sub.TrialEndsAtUtc = null;
        var baseStart = sub.CurrentPeriodEndUtc > now ? sub.CurrentPeriodEndUtc : now;
        sub.CurrentPeriodStartUtc = now;
        sub.CurrentPeriodEndUtc = baseStart.AddMonths(1);

        _dbContext.SubscriptionInvoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return Result.Success(new SubscriptionInvoiceDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.Amount,
            invoice.Currency,
            invoice.Status,
            invoice.PaymentChannel,
            invoice.PaymentReference,
            invoice.PaidAtUtc,
            invoice.Notes));
    }

    public async Task<IReadOnlyList<SubscriptionInvoiceDto>> ListInvoicesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SubscriptionInvoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new SubscriptionInvoiceDto(
                i.Id,
                i.InvoiceNumber,
                i.Amount,
                i.Currency,
                i.Status,
                i.PaymentChannel,
                i.PaymentReference,
                i.PaidAtUtc,
                i.Notes))
            .ToListAsync(cancellationToken);
    }

    public static bool CanTransition(string fromStatus, string toStatus)
    {
        if (string.Equals(fromStatus, toStatus, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (fromStatus, toStatus) switch
        {
            (SubscriptionStatusConstants.Trial, SubscriptionStatusConstants.Active) => true,
            (SubscriptionStatusConstants.Trial, SubscriptionStatusConstants.Suspended) => true,
            (SubscriptionStatusConstants.Active, SubscriptionStatusConstants.PastDue) => true,
            (SubscriptionStatusConstants.Active, SubscriptionStatusConstants.Cancelled) => true,
            (SubscriptionStatusConstants.PastDue, SubscriptionStatusConstants.Active) => true,
            (SubscriptionStatusConstants.PastDue, SubscriptionStatusConstants.Suspended) => true,
            (SubscriptionStatusConstants.PastDue, SubscriptionStatusConstants.Cancelled) => true,
            (SubscriptionStatusConstants.Suspended, SubscriptionStatusConstants.Active) => true,
            (SubscriptionStatusConstants.Suspended, SubscriptionStatusConstants.Cancelled) => true,
            _ => false
        };
    }

    internal static TenantSubscriptionSnapshot MapSnapshot(TenantSubscription sub, SubscriptionPlan plan, DateTimeOffset utcNow)
    {
        var modules = ParseModules(plan.EnabledModulesJson);
        var isPastDue = string.Equals(sub.Status, SubscriptionStatusConstants.PastDue, StringComparison.OrdinalIgnoreCase);
        var isReadOnly = isPastDue && (sub.ReadOnlySinceUtc.HasValue || (sub.GracePeriodEndsAtUtc.HasValue && utcNow >= sub.GracePeriodEndsAtUtc.Value));
        var isGraceWarning = isPastDue && !isReadOnly;

        return new TenantSubscriptionSnapshot(
            sub.TenantId,
            plan.Code,
            plan.Name,
            sub.Status,
            isReadOnly,
            isGraceWarning,
            modules,
            sub.OverrideMaxEmployees ?? plan.MaxActiveEmployees,
            sub.OverrideMaxStorageBytes ?? plan.MaxStorageBytes,
            plan.MaxApiClients,
            sub.CurrentPeriodStartUtc,
            sub.CurrentPeriodEndUtc,
            sub.TrialEndsAtUtc,
            sub.GracePeriodEndsAtUtc,
            sub.ReadOnlySinceUtc);
    }

    internal static IReadOnlySet<string> ParseModules(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            return new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
