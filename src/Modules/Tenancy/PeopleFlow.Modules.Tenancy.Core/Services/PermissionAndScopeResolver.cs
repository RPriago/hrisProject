using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class PermissionAndScopeResolver : IPermissionAndScopeResolver
{
    private readonly TenancyDbContext _db;
    private readonly IClock _clock;

    public PermissionAndScopeResolver(TenancyDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<EffectiveAccessPolicy?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || tenant.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var _ = _db.BeginOperationTenantScope(tenantId);

        var membership = await _db.TenancyMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId && m.Status == "Active", cancellationToken);
        if (membership is null)
        {
            return null;
        }

        var now = _clock.UtcNow;
        var allAssignments = await _db.TenancyMemberRoleScopes
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.MembershipId == membership.Id)
            .ToListAsync(cancellationToken);

        var assignments = allAssignments
            .Where(s => s.ValidUntilUtc is null || s.ValidUntilUtc > now)
            .ToList();

        if (assignments.Count == 0)
        {
            return null;
        }

        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var roles = await _db.TenancyRoles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var permissions = await _db.TenancyRolePermissions
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && roleIds.Contains(p.RoleId))
            .ToListAsync(cancellationToken);

        var roleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeRoles = new List<(string Code, bool IsReadOnly)>();
        var slicesByPerm = new Dictionary<string, List<EffectivePermissionScopeSlice>>(StringComparer.Ordinal);

        foreach (var assignment in assignments)
        {
            if (!roles.TryGetValue(assignment.RoleId, out var role))
            {
                continue;
            }

            if (!RoleScopeAssignmentValidator.TryParseDataScope(assignment.ScopeType, out var scope))
            {
                // Fail closed on corrupt/undefined scope values rather than defaulting to Self.
                continue;
            }

            roleCodes.Add(role.Code);
            activeRoles.Add((role.Code, role.IsReadOnlyRole));

            var targetIds = ParseTargetIds(assignment.ScopeTargetIdsJson);

            foreach (var perm in permissions.Where(p => p.RoleId == role.Id))
            {
                var effectivePermScope = perm.PermissionCode.EndsWith(".read_own", StringComparison.Ordinal)
                    ? DataScope.Self
                    : scope;

                if (!slicesByPerm.TryGetValue(perm.PermissionCode, out var slices))
                {
                    slices = [];
                    slicesByPerm[perm.PermissionCode] = slices;
                }

                var matchingIndex = slices.FindIndex(s =>
                    s.Scope == effectivePermScope &&
                    s.DataPeriodFrom == assignment.DataPeriodFrom &&
                    s.DataPeriodTo == assignment.DataPeriodTo);

                if (matchingIndex >= 0)
                {
                    var existingSlice = slices[matchingIndex];
                    var coalescedTargets = new HashSet<Guid>(existingSlice.ScopedTargetIds);
                    coalescedTargets.UnionWith(targetIds);
                    slices[matchingIndex] = existingSlice with { ScopedTargetIds = coalescedTargets };
                }
                else
                {
                    slices.Add(new EffectivePermissionScopeSlice(
                        effectivePermScope,
                        new HashSet<Guid>(targetIds),
                        assignment.DataPeriodFrom,
                        assignment.DataPeriodTo));
                }
            }
        }

        if (activeRoles.Count == 0)
        {
            return null;
        }

        var grants = new Dictionary<string, EffectivePermissionGrant>(StringComparer.Ordinal);
        foreach (var (permCode, slices) in slicesByPerm)
        {
            var maxScope = slices[0].Scope;
            var mergedTargets = new HashSet<Guid>(slices[0].ScopedTargetIds);
            var mergedFrom = slices[0].DataPeriodFrom;
            var mergedTo = slices[0].DataPeriodTo;

            for (var i = 1; i < slices.Count; i++)
            {
                var s = slices[i];
                if (s.Scope > maxScope)
                {
                    maxScope = s.Scope;
                }

                mergedTargets.UnionWith(s.ScopedTargetIds);
                mergedFrom = MergeFromDate(mergedFrom, s.DataPeriodFrom);
                mergedTo = MergeToDate(mergedTo, s.DataPeriodTo);
            }

            grants[permCode] = new EffectivePermissionGrant(
                permCode,
                maxScope,
                mergedTargets,
                mergedFrom,
                mergedTo,
                slices.AsReadOnly());
        }

        var isReadOnlyRole = activeRoles.All(r => r.IsReadOnly);

        return new EffectiveAccessPolicy(
            tenantId,
            userId,
            isReadOnlyRole,
            tenant.EnforcePayrollSod,
            roleCodes,
            grants);
    }

    private static DateOnly? MergeFromDate(DateOnly? existing, DateOnly? incoming)
    {
        if (existing is null) return incoming;
        if (incoming is null) return existing;
        return existing.Value >= incoming.Value ? existing : incoming;
    }

    private static DateOnly? MergeToDate(DateOnly? existing, DateOnly? incoming)
    {
        if (existing is null) return incoming;
        if (incoming is null) return existing;
        return existing.Value <= incoming.Value ? existing : incoming;
    }

    private static HashSet<Guid> ParseTargetIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HashSet<Guid>();
        try
        {
            var ids = JsonSerializer.Deserialize<Guid[]>(json);
            return ids is null ? new HashSet<Guid>() : new HashSet<Guid>(ids);
        }
        catch (JsonException)
        {
            return new HashSet<Guid>();
        }
    }
}
