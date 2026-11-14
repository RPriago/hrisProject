using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class TenantMembershipReader : ITenantMembershipReader
{
    private readonly TenancyDbContext _db;
    private readonly IClock _clock;

    public TenantMembershipReader(TenancyDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    private sealed class MembershipLookupRow
    {
        public Guid TenantId { get; init; }
        public string TenantSlug { get; init; } = string.Empty;
        public string TenantName { get; init; } = string.Empty;
        public string MembershipStatus { get; init; } = string.Empty;
        public bool RequireMfaForAll { get; init; }
        public bool EnforcePayrollSod { get; init; }
        public string TenantStatus { get; init; } = string.Empty;
        public bool IsEmergencyAccount { get; init; }
    }

    public async Task<IReadOnlyList<UserTenantMembershipSummary>> GetActiveMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsNpgsql())
        {
            var rows = await _db.Database
                .SqlQueryRaw<MembershipLookupRow>(
                    """
                    SELECT tenant_id AS "TenantId",
                           tenant_slug AS "TenantSlug",
                           tenant_name AS "TenantName",
                           membership_status AS "MembershipStatus",
                           require_mfa_for_all AS "RequireMfaForAll",
                           enforce_payroll_sod AS "EnforcePayrollSod",
                           tenant_status AS "TenantStatus",
                           is_emergency_account AS "IsEmergencyAccount"
                    FROM fn_lookup_active_memberships_by_user({0})
                    """,
                    userId)
                .ToListAsync(cancellationToken);

            return rows.Select(r => new UserTenantMembershipSummary(
                    r.TenantId,
                    r.TenantSlug,
                    r.TenantName,
                    r.MembershipStatus,
                    r.RequireMfaForAll,
                    r.EnforcePayrollSod,
                    r.TenantStatus,
                    r.IsEmergencyAccount))
                .ToList();
        }

        var query =
            from m in _db.TenancyMemberships.IgnoreQueryFilters().AsNoTracking()
            join t in _db.Tenants.AsNoTracking() on m.TenantId equals t.Id
            where m.UserId == userId
               && m.Status == "Active"
               && m.RevokedAtUtc == null
               && t.Status != "Suspended"
            orderby t.DisplayName
            select new UserTenantMembershipSummary(
                t.Id,
                t.Slug,
                t.DisplayName,
                m.Status,
                t.RequireMfaForAll,
                t.EnforcePayrollSod,
                t.Status,
                m.IsEmergencyAccount);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<UserTenantMembershipSummary?> GetActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var query =
            from m in _db.TenancyMemberships.AsNoTracking()
            join t in _db.Tenants.AsNoTracking() on m.TenantId equals t.Id
            where m.TenantId == tenantId
               && m.UserId == userId
               && m.Status == "Active"
               && m.RevokedAtUtc == null
            select new UserTenantMembershipSummary(
                t.Id,
                t.Slug,
                t.DisplayName,
                m.Status,
                t.RequireMfaForAll,
                t.EnforcePayrollSod,
                t.Status,
                m.IsEmergencyAccount);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsTenantMfaRequiredAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.RequireMfaForAll)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ActiveSupportGrantInfo?> GetActiveSupportGrantAsync(Guid grantId, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var grant = await _db.SupportAccessGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == grantId
                     && (g.AssignedAdminUserId == null || g.AssignedAdminUserId == adminUserId)
                     && g.RevokedAtUtc == null, cancellationToken);

        var now = _clock.UtcNow;
        if (grant is null || grant.StartsAtUtc > now || grant.ExpiresAtUtc <= now)
        {
            return null;
        }

        return new ActiveSupportGrantInfo(
            grant.Id,
            grant.TenantId,
            grant.AccessLevel,
            grant.AllowFinancialRead,
            grant.ExpiresAtUtc);
    }
}
