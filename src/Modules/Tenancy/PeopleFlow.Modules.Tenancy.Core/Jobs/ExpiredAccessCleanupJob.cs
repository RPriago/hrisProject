using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Jobs.Filters;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Persistence.Audit;

namespace PeopleFlow.Modules.Tenancy.Core.Jobs;

[PlatformSystemJob]
public sealed class ExpiredAccessCleanupJob
{
    private readonly TenancyDbContext _db;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IClock _clock;
    private readonly ISessionContextResolver? _sessionResolver;

    public ExpiredAccessCleanupJob(
        TenancyDbContext db,
        IPlatformAuditLogWriter auditWriter,
        IClock clock,
        ISessionContextResolver? sessionResolver = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sessionResolver = sessionResolver;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // 1. Platform-owned SupportAccessGrants cleanup
        var unrevokedGrants = await _db.SupportAccessGrants
            .Where(g => g.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        var expiredGrants = unrevokedGrants.Where(g => g.ExpiresAtUtc <= now).ToList();
        foreach (var grant in expiredGrants)
        {
            grant.RevokedAtUtc = now;
        }

        if (expiredGrants.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var grant in expiredGrants)
            {
                if (_sessionResolver is not null)
                {
                    await _sessionResolver.ClearSupportGrantSessionsAsync(grant.Id, cancellationToken);
                }

                await _auditWriter.WriteAsync(new PlatformAuditEntry(
                    null,
                    "SystemJob",
                    "SUPPORT_GRANT_EXPIRED_CLEANUP",
                    TargetTenantId: grant.TenantId,
                    SupportGrantId: grant.Id,
                    ResourcePath: nameof(ExpiredAccessCleanupJob)), cancellationToken);
            }
        }

        // 2. Per-tenant RLS-safe sweep for expired invitations & time-bound role assignments
        var tenantIds = await _db.Tenants
            .AsNoTracking()
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            using var _ = _db.BeginOperationTenantScope(tenantId);

            var pendingInvitations = await _db.TenancyInvitations
                .Where(i => i.TenantId == tenantId && i.Status == "Pending")
                .ToListAsync(cancellationToken);

            var expiredInvitations = pendingInvitations.Where(i => i.ExpiresAtUtc <= now).ToList();
            foreach (var inv in expiredInvitations)
            {
                inv.Status = "Expired";
            }

            var timeBoundScopes = await _db.TenancyMemberRoleScopes
                .Where(s => s.TenantId == tenantId && s.ValidUntilUtc != null)
                .ToListAsync(cancellationToken);

            var expiredScopes = timeBoundScopes.Where(s => s.ValidUntilUtc <= now).ToList();
            if (expiredScopes.Count > 0)
            {
                _db.TenancyMemberRoleScopes.RemoveRange(expiredScopes);
            }

            if (expiredInvitations.Count > 0 || expiredScopes.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);

                if (expiredScopes.Count > 0)
                {
                    await _auditWriter.WriteAsync(new PlatformAuditEntry(
                        null,
                        "SystemJob",
                        "TEMPORARY_ROLE_SCOPE_EXPIRED_CLEANUP",
                        TargetTenantId: tenantId,
                        ResourcePath: nameof(ExpiredAccessCleanupJob),
                        MetadataJson: JsonSerializer.Serialize(new { removedCount = expiredScopes.Count })), cancellationToken);
                }
            }
        }
    }
}
