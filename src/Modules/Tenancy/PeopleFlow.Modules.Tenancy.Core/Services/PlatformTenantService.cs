using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Outbox;
using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed partial class PlatformTenantService : IPlatformTenantService
{
    private readonly TenancyDbContext _db;
    private readonly TenantRoleSeeder _roleSeeder;
    private readonly IIdentityProvisioningService _identityProvisioning;
    private readonly ISubscriptionLifecycleService _subscriptionLifecycle;
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IOutboxPublisher _outbox;
    private readonly IClock _clock;

    public PlatformTenantService(
        TenancyDbContext db,
        TenantRoleSeeder roleSeeder,
        IIdentityProvisioningService identityProvisioning,
        ISubscriptionLifecycleService subscriptionLifecycle,
        ITenantSubscriptionQueryService subscriptionQuery,
        IPlatformAuditLogWriter auditWriter,
        IOutboxPublisher outbox,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _roleSeeder = roleSeeder ?? throw new ArgumentNullException(nameof(roleSeeder));
        _identityProvisioning = identityProvisioning ?? throw new ArgumentNullException(nameof(identityProvisioning));
        _subscriptionLifecycle = subscriptionLifecycle ?? throw new ArgumentNullException(nameof(subscriptionLifecycle));
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<PlatformTenantSummaryDto>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = (await _db.Tenants.AsNoTracking().ToListAsync(cancellationToken))
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToList();

        var results = new List<PlatformTenantSummaryDto>(tenants.Count);
        foreach (var t in tenants)
        {
            var snap = await _subscriptionQuery.GetSnapshotAsync(t.Id, cancellationToken);
            results.Add(new PlatformTenantSummaryDto(
                t.Id,
                t.Slug,
                t.DisplayName,
                t.IndustryPresetCode,
                t.Status,
                snap?.PlanCode,
                snap?.Status,
                t.RequireMfaForAll,
                t.CreatedAtUtc));
        }

        return results;
    }

    public async Task<Result<ProvisionedTenantDto>> ProvisionTenantAsync(
        Guid platformAdminUserId,
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (platformAdminUserId == Guid.Empty)
        {
            return Result.Failure<ProvisionedTenantDto>(new Error("INVALID_PLATFORM_ADMIN", "Platform admin user ID is required."));
        }

        var normalizedSlug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SlugRegex().IsMatch(normalizedSlug))
        {
            return Result.Failure<ProvisionedTenantDto>(new Error("INVALID_SLUG", "Slug must be 3-64 lowercase alphanumeric characters or hyphens."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 160)
        {
            return Result.Failure<ProvisionedTenantDto>(new Error("INVALID_NAME", "DisplayName (1..160 characters) is required."));
        }

        if (!IndustryPresets.All.Contains(request.IndustryPresetCode))
        {
            return Result.Failure<ProvisionedTenantDto>(new Error("INVALID_INDUSTRY_PRESET", $"Unknown industry preset '{request.IndustryPresetCode}'."));
        }

        var trimmedEmail = request.OwnerEmail?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedEmail) ||
            trimmedEmail.Length > 256 ||
            !MailAddress.TryCreate(trimmedEmail, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, trimmedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ProvisionedTenantDto>(new Error("INVALID_OWNER_EMAIL", "A valid OwnerEmail (<=256 characters) is required."));
        }

        var planValidation = await _subscriptionLifecycle.ValidateActivePlanAsync(request.InitialPlanCode, cancellationToken);
        if (!planValidation.IsSuccess)
        {
            return Result.Failure<ProvisionedTenantDto>(planValidation.Error);
        }

        if (await _db.Tenants.AnyAsync(t => t.Slug == normalizedSlug, cancellationToken))
        {
            return Result.Failure<ProvisionedTenantDto>(new Error("SLUG_EXISTS", $"Tenant slug '{normalizedSlug}' already exists."));
        }

        var ownerResult = await _identityProvisioning.FindOrCreateUserAsync(
            trimmedEmail,
            request.OwnerInitialPassword,
            string.IsNullOrWhiteSpace(request.DefaultLanguage) ? "id-ID" : request.DefaultLanguage.Trim(),
            cancellationToken: cancellationToken);
        if (!ownerResult.IsSuccess)
        {
            return Result.Failure<ProvisionedTenantDto>(ownerResult.Error);
        }

        var tenant = new Tenant(Guid.CreateVersion7())
        {
            Slug = normalizedSlug,
            DisplayName = request.DisplayName.Trim(),
            IndustryPresetCode = request.IndustryPresetCode,
            DefaultLanguage = string.IsNullOrWhiteSpace(request.DefaultLanguage) ? "id-ID" : request.DefaultLanguage.Trim(),
            DefaultTimeZoneId = string.IsNullOrWhiteSpace(request.DefaultTimeZoneId) ? "Asia/Jakarta" : request.DefaultTimeZoneId.Trim(),
            EnforcePayrollSod = true,
            Status = "Active"
        };

        try
        {
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return Result.Failure<ProvisionedTenantDto>(new Error("SLUG_EXISTS", $"Tenant slug '{normalizedSlug}' already exists."));
        }

        var subscriptionProvisioned = false;
        try
        {
            var subResult = await _subscriptionLifecycle.ProvisionInitialAsync(
                tenant.Id,
                request.InitialPlanCode,
                request.StartAsTrial,
                cancellationToken);
            if (!subResult.IsSuccess)
            {
                await CompensateProvisioningAsync(tenant.Id, subscriptionProvisioned: false);
                return Result.Failure<ProvisionedTenantDto>(subResult.Error);
            }

            subscriptionProvisioned = true;

            using (var _ = _db.BeginOperationTenantScope(tenant.Id))
            {
                await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                await _roleSeeder.SeedDefaultRolesAsync(tenant.Id, cancellationToken);

                var ownerRole = await _db.TenancyRoles
                    .FirstAsync(r => r.TenantId == tenant.Id && r.Code == RoleCodes.CompanyOwner, cancellationToken);

                var membership = new TenancyMembership(Guid.CreateVersion7())
                {
                    TenantId = tenant.Id,
                    UserId = ownerResult.Value.UserId,
                    Status = "Active",
                    JoinedAtUtc = _clock.UtcNow
                };
                _db.TenancyMemberships.Add(membership);
                _db.TenancyMemberRoleScopes.Add(new TenancyMemberRoleScope(Guid.CreateVersion7())
                {
                    TenantId = tenant.Id,
                    MembershipId = membership.Id,
                    RoleId = ownerRole.Id,
                    ScopeType = nameof(DataScope.Tenant)
                });
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }

            await _outbox.PublishRawAsync(
                "Tenancy.TenantProvisioned",
                JsonSerializer.Serialize(new { tenant.Id, tenant.Slug, tenant.IndustryPresetCode, ownerUserId = ownerResult.Value.UserId }),
                tenantId: tenant.Id,
                autoSave: true,
                cancellationToken: cancellationToken);

            await _auditWriter.WriteAsync(new PlatformAuditEntry(
                platformAdminUserId,
                "PlatformAdmin",
                "TENANT_PROVISIONED",
                TargetTenantId: tenant.Id,
                ResourcePath: "/api/v1/platform/tenants",
                MetadataJson: JsonSerializer.Serialize(new { tenant.Slug, planCode = request.InitialPlanCode, request.StartAsTrial })), cancellationToken);

            return Result.Success(new ProvisionedTenantDto(
                tenant.Id,
                tenant.Slug,
                tenant.DisplayName,
                subResult.Value.PlanCode,
                subResult.Value.Status,
                ownerResult.Value.UserId));
        }
        catch
        {
            await CompensateProvisioningAsync(tenant.Id, subscriptionProvisioned);
            throw;
        }
    }

    private async Task CompensateProvisioningAsync(Guid tenantId, bool subscriptionProvisioned)
    {
        try
        {
            _db.ChangeTracker.Clear();
            if (subscriptionProvisioned)
            {
                await _subscriptionLifecycle.CompensateInitialProvisionAsync(tenantId, CancellationToken.None);
            }

            using var _ = _db.BeginOperationTenantScope(tenantId);
            await _db.TenancyMemberRoleScopes.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync(CancellationToken.None);
            await _db.TenancyMemberships.Where(m => m.TenantId == tenantId).ExecuteDeleteAsync(CancellationToken.None);
            await _db.TenancyRolePermissions.Where(p => p.TenantId == tenantId).ExecuteDeleteAsync(CancellationToken.None);
            await _db.TenancyRoles.Where(r => r.TenantId == tenantId).ExecuteDeleteAsync(CancellationToken.None);
            await _db.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync(CancellationToken.None);
        }
        catch
        {
            // Never mask the original provisioning failure with compensation cleanup errors.
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return true;
        }

        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Result> SuspendTenantAsync(Guid platformAdminUserId, Guid tenantId, string reason, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure(new Error("TENANT_NOT_FOUND", "Tenant was not found."));
        }

        tenant.Status = "Suspended";
        await _db.SaveChangesAsync(cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            platformAdminUserId,
            "PlatformAdmin",
            "TENANT_SUSPENDED",
            TargetTenantId: tenantId,
            ResourcePath: $"/api/v1/platform/tenants/{tenantId}/suspend",
            Reason: reason), cancellationToken);

        return Result.Success();
    }

    public async Task<Result> ReactivateTenantAsync(Guid platformAdminUserId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure(new Error("TENANT_NOT_FOUND", "Tenant was not found."));
        }

        tenant.Status = "Active";
        await _db.SaveChangesAsync(cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            platformAdminUserId,
            "PlatformAdmin",
            "TENANT_REACTIVATED",
            TargetTenantId: tenantId,
            ResourcePath: $"/api/v1/platform/tenants/{tenantId}/reactivate"), cancellationToken);

        return Result.Success();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$")]
    private static partial Regex SlugRegex();
}
