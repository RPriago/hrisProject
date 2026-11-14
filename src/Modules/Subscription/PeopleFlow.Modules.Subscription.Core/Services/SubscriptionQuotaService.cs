using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Persistence;

namespace PeopleFlow.Modules.Subscription.Core.Services;

public sealed class SubscriptionQuotaService : ISubscriptionQuotaService
{
    private readonly SubscriptionDbContext _dbContext;

    public SubscriptionQuotaService(SubscriptionDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Result> CheckEmployeeQuotaAsync(Guid tenantId, int currentActiveCount, CancellationToken cancellationToken = default)
    {
        if (currentActiveCount < 0)
        {
            return Result.Failure(Error.Validation("QUOTA_INPUT_NEGATIVE", "Active employee count cannot be negative."));
        }

        var limitsResult = await ResolveLimitsAsync(tenantId, cancellationToken);
        if (limitsResult.IsFailure)
        {
            return Result.Failure(limitsResult.Error);
        }

        var limits = limitsResult.Value;
        if (currentActiveCount >= limits.MaxEmployees)
        {
            return Result.Failure(Error.Forbidden("PLAN_QUOTA_EXCEEDED", $"Employee limit ({limits.MaxEmployees}) reached for plan '{limits.PlanCode}'."));
        }

        return Result.Success();
    }

    public async Task<Result> CheckStorageQuotaAsync(Guid tenantId, long currentUsedBytes, long additionalBytes, CancellationToken cancellationToken = default)
    {
        if (currentUsedBytes < 0 || additionalBytes < 0)
        {
            return Result.Failure(Error.Validation("QUOTA_INPUT_NEGATIVE", "Storage byte counts cannot be negative."));
        }

        var limitsResult = await ResolveLimitsAsync(tenantId, cancellationToken);
        if (limitsResult.IsFailure)
        {
            return Result.Failure(limitsResult.Error);
        }

        var limits = limitsResult.Value;
        if (limits.MaxStorageBytes < 0 ||
            additionalBytes > limits.MaxStorageBytes ||
            currentUsedBytes > limits.MaxStorageBytes - additionalBytes)
        {
            return Result.Failure(Error.Forbidden("STORAGE_QUOTA_EXCEEDED", $"Storage limit ({limits.MaxStorageBytes} bytes) exceeded."));
        }

        return Result.Success();
    }

    public async Task<Result> CheckApiClientQuotaAsync(Guid tenantId, int currentActiveClientCount, CancellationToken cancellationToken = default)
    {
        if (currentActiveClientCount < 0)
        {
            return Result.Failure(Error.Validation("QUOTA_INPUT_NEGATIVE", "Active API client count cannot be negative."));
        }

        var limitsResult = await ResolveLimitsAsync(tenantId, cancellationToken);
        if (limitsResult.IsFailure)
        {
            return Result.Failure(limitsResult.Error);
        }

        var limits = limitsResult.Value;
        if (currentActiveClientCount >= limits.MaxApiClients)
        {
            return Result.Failure(Error.Forbidden("API_CLIENT_QUOTA_EXCEEDED", $"API client limit ({limits.MaxApiClients}) reached for plan '{limits.PlanCode}'."));
        }

        return Result.Success();
    }

    private async Task<Result<(string PlanCode, int MaxEmployees, long MaxStorageBytes, int MaxApiClients)>> ResolveLimitsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsNpgsql() && _dbContext.Database.CurrentTransaction != null)
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM subscription_tenant_subscriptions WHERE tenant_id = {tenantId} FOR UPDATE",
                cancellationToken);
        }

        var joined = await (
            from sub in _dbContext.TenantSubscriptions.AsNoTracking()
            join plan in _dbContext.SubscriptionPlans.AsNoTracking() on sub.PlanId equals plan.Id
            where sub.TenantId == tenantId
            select new
            {
                sub.Status,
                plan.Code,
                MaxEmployees = sub.OverrideMaxEmployees ?? plan.MaxActiveEmployees,
                MaxStorage = sub.OverrideMaxStorageBytes ?? plan.MaxStorageBytes,
                plan.MaxApiClients
            }).SingleOrDefaultAsync(cancellationToken);

        if (joined == null)
        {
            return Result.Failure<(string, int, long, int)>(
                Error.Forbidden("TENANT_SUBSCRIPTION_MISSING", "No active subscription found for tenant."));
        }

        if (!string.Equals(joined.Status, SubscriptionStatusConstants.Trial, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(joined.Status, SubscriptionStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<(string, int, long, int)>(
                Error.Forbidden("TENANT_SUBSCRIPTION_INACTIVE", $"Subscription is in '{joined.Status}' status and cannot allocate new quota."));
        }

        return Result.Success((joined.Code, joined.MaxEmployees, joined.MaxStorage, joined.MaxApiClients));
    }
}
