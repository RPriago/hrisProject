using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Contracts;

public sealed record EffectivePermissionScopeSlice(
    DataScope Scope,
    IReadOnlySet<Guid> ScopedTargetIds,
    DateOnly? DataPeriodFrom,
    DateOnly? DataPeriodTo);

public sealed record EffectivePermissionGrant(
    string PermissionCode,
    DataScope MaxScope,
    IReadOnlySet<Guid> ScopedTargetIds,
    DateOnly? DataPeriodFrom,
    DateOnly? DataPeriodTo,
    IReadOnlyList<EffectivePermissionScopeSlice>? Scopes = null)
{
    public bool IsSatisfiedBy(DataScope requestedScope, Guid? targetId = null, DateOnly? recordDate = null)
    {
        var slices = Scopes is { Count: > 0 }
            ? Scopes
            : [new EffectivePermissionScopeSlice(MaxScope, ScopedTargetIds, DataPeriodFrom, DataPeriodTo)];

        foreach (var slice in slices)
        {
            if (slice.Scope < requestedScope)
            {
                continue;
            }

            if (slice.Scope is DataScope.Branch or DataScope.LegalEntity or DataScope.Team &&
                (!targetId.HasValue || !slice.ScopedTargetIds.Contains(targetId.Value)))
            {
                continue;
            }

            if (recordDate.HasValue)
            {
                if (slice.DataPeriodFrom.HasValue && recordDate.Value < slice.DataPeriodFrom.Value)
                {
                    continue;
                }

                if (slice.DataPeriodTo.HasValue && recordDate.Value > slice.DataPeriodTo.Value)
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }
}

public sealed record EffectiveAccessPolicy(
    Guid TenantId,
    Guid UserId,
    bool IsReadOnlyRole,
    bool EnforcePayrollSod,
    IReadOnlySet<string> RoleCodes,
    IReadOnlyDictionary<string, EffectivePermissionGrant> GrantsByPermission)
{
    public bool HasPermission(string permissionCode) => GrantsByPermission.ContainsKey(permissionCode);
}

public interface IPermissionAndScopeResolver
{
    Task<EffectiveAccessPolicy?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
