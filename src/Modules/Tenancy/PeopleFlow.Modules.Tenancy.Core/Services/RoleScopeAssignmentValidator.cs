using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

internal static class RoleScopeAssignmentValidator
{
    public static async Task<Result> ValidateAssignmentsAsync(
        TenancyDbContext db,
        IPermissionAndScopeResolver permissionResolver,
        IOrganizationUnitValidator orgValidator,
        Guid tenantId,
        Guid actorUserId,
        IReadOnlyList<RoleScopeAssignmentInput> assignments,
        CancellationToken cancellationToken,
        EffectiveAccessPolicy? preResolvedActorPolicy = null)
    {
        if (assignments is null || assignments.Count == 0)
        {
            return Result.Failure(new Error("INVALID_ROLE_ASSIGNMENTS", "At least one role assignment is required."));
        }

        var actorPolicy = preResolvedActorPolicy ?? await permissionResolver.ResolveAsync(tenantId, actorUserId, cancellationToken);
        if (actorPolicy is null || actorPolicy.IsReadOnlyRole)
        {
            return Result.Failure(new Error("PRIVILEGE_ESCALATION_DENIED", "Actor does not hold mutable permissions in this tenant."));
        }

        using var _ = db.BeginOperationTenantScope(tenantId);

        var requestedRoleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var roles = await db.TenancyRoles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && requestedRoleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (roles.Count != requestedRoleIds.Count)
        {
            return Result.Failure(new Error("ROLE_NOT_FOUND", "One or more referenced roles do not exist in this tenant."));
        }

        var rolePermissions = await db.TenancyRolePermissions
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && requestedRoleIds.Contains(p.RoleId))
            .Select(p => p.PermissionCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var permission in rolePermissions)
        {
            if (!actorPolicy.HasPermission(permission))
            {
                return Result.Failure(new Error(
                    "PRIVILEGE_ESCALATION_DENIED",
                    $"Cannot assign role containing permission '{permission}' that actor does not hold."));
            }
        }

        foreach (var item in assignments)
        {
            if (!TryParseDataScope(item.ScopeType, out var scope))
            {
                return Result.Failure(new Error("INVALID_DATA_SCOPE", $"Unknown data scope '{item.ScopeType}'."));
            }

            if (item.DataPeriodFrom.HasValue && item.DataPeriodTo.HasValue && item.DataPeriodFrom > item.DataPeriodTo)
            {
                return Result.Failure(new Error("INVALID_DATA_PERIOD", "DataPeriodFrom must not exceed DataPeriodTo."));
            }

            var targetIds = item.ScopeTargetIds?.ToArray() ?? Array.Empty<Guid>();
            var targetsValid = await orgValidator.AllScopeTargetsExistAsync(tenantId, scope, targetIds, cancellationToken);
            if (!targetsValid)
            {
                return Result.Failure(new Error("INVALID_SCOPE_TARGETS", "One or more scope target unit IDs are invalid."));
            }
        }

        return Result.Success();
    }

    public static async Task<Result> EnsureActorCanManageTargetMemberAsync(
        TenancyDbContext db,
        IPermissionAndScopeResolver permissionResolver,
        Guid tenantId,
        Guid actorUserId,
        Guid targetMembershipId,
        DateTimeOffset utcNow,
        EffectiveAccessPolicy? preResolvedActorPolicy = null,
        CancellationToken cancellationToken = default)
    {
        var actorPolicy = preResolvedActorPolicy ?? await permissionResolver.ResolveAsync(tenantId, actorUserId, cancellationToken);
        if (actorPolicy is null || actorPolicy.IsReadOnlyRole)
        {
            return Result.Failure(Error.Forbidden("PRIVILEGE_ESCALATION_FORBIDDEN", "Actor does not hold mutable permissions in this tenant."));
        }

        var rawTargetPermissions = await (
            from s in db.TenancyMemberRoleScopes.AsNoTracking()
            join rp in db.TenancyRolePermissions.AsNoTracking() on s.RoleId equals rp.RoleId
            where s.TenantId == tenantId
               && s.MembershipId == targetMembershipId
               && rp.TenantId == tenantId
            select new { rp.PermissionCode, s.ValidUntilUtc })
            .ToListAsync(cancellationToken);

        var targetPermissions = rawTargetPermissions
            .Where(x => x.ValidUntilUtc is null || x.ValidUntilUtc > utcNow)
            .Select(x => x.PermissionCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var permission in targetPermissions)
        {
            if (!actorPolicy.HasPermission(permission))
            {
                return Result.Failure(Error.Forbidden(
                    "PRIVILEGE_ESCALATION_FORBIDDEN",
                    $"Cannot modify or revoke a member holding permission '{permission}' that you do not hold."));
            }
        }

        return Result.Success();
    }

    internal static bool TryParseDataScope(string? scopeType, out DataScope scope)
    {
        if (string.IsNullOrWhiteSpace(scopeType))
        {
            scope = default;
            return false;
        }

        var trimmed = scopeType.Trim();
        if (long.TryParse(trimmed, out _))
        {
            scope = default;
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out scope) && Enum.IsDefined(scope);
    }
}
