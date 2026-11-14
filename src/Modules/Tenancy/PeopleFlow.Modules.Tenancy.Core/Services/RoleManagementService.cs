using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.Modules.Tenancy.Core.Persistence;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed partial class RoleManagementService : IRoleManagementService
{
    private const int MaxRoleCodeLength = 64;
    private const int MaxRoleNameLength = 120;
    private const int MaxRoleDescriptionLength = 400;

    private static readonly IReadOnlySet<string> ValidCatalog =
        new HashSet<string>(Permissions.AllCatalog, StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ReservedSystemRoleCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RoleCodes.CompanyOwner,
            RoleCodes.HrAdmin,
            RoleCodes.PayrollOfficer,
            RoleCodes.Manager,
            RoleCodes.Employee,
            RoleCodes.Auditor
        };

    [GeneratedRegex("^[A-Za-z0-9_.-]{2,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidRoleCodeRegex();

    private readonly TenancyDbContext _db;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;

    public RoleManagementService(
        TenancyDbContext db,
        IPermissionAndScopeResolver permissionResolver,
        ITenantSubscriptionQueryService subscriptionQuery)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var roles = await _db.TenancyRoles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.IsSystemBuiltin)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var perms = await _db.TenancyRolePermissions
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var lookup = perms.ToLookup(p => p.RoleId, p => p.PermissionCode);

        return roles
            .Select(r => new RoleDto(
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.IsSystemBuiltin,
                r.IsReadOnlyRole,
                lookup[r.Id].OrderBy(c => c, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    public async Task<Result<RoleDto>> CreateCustomRoleAsync(
        Guid tenantId,
        Guid actorUserId,
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(tenantId, actorUserId, request, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.Failure<RoleDto>(validation.Error);
        }

        using var _ = _db.BeginOperationTenantScope(tenantId);
        var normalizedCode = request.Code.Trim();
        var codeUpper = normalizedCode.ToUpperInvariant();
        var distinctPermissions = request.Permissions.Distinct(StringComparer.Ordinal).ToList();

        try
        {
            return await TenantTransactionSerializer.ExecuteAsync(_db, tenantId, async () =>
            {
                var exists = await _db.TenancyRoles.AnyAsync(
                    r => r.TenantId == tenantId && r.Code.ToUpper() == codeUpper,
                    cancellationToken);
                if (exists)
                {
                    return Result.Failure<RoleDto>(new Error("ROLE_CODE_EXISTS", $"Role code '{normalizedCode}' already exists in this tenant."));
                }

                var role = new TenancyRole(Guid.CreateVersion7())
                {
                    TenantId = tenantId,
                    Code = normalizedCode,
                    Name = request.Name.Trim(),
                    Description = request.Description?.Trim(),
                    IsSystemBuiltin = false,
                    IsReadOnlyRole = request.IsReadOnlyRole
                };

                _db.TenancyRoles.Add(role);
                foreach (var permission in distinctPermissions)
                {
                    _db.TenancyRolePermissions.Add(new TenancyRolePermission
                    {
                        TenantId = tenantId,
                        RoleId = role.Id,
                        PermissionCode = permission
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);

                return Result.Success(new RoleDto(
                    role.Id,
                    role.Code,
                    role.Name,
                    role.Description,
                    role.IsSystemBuiltin,
                    role.IsReadOnlyRole,
                    distinctPermissions));
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Result.Failure<RoleDto>(new Error("ROLE_CODE_EXISTS", $"Role code '{normalizedCode}' already exists in this tenant."));
        }
    }

    public async Task<Result<RoleDto>> UpdateRoleAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid roleId,
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSaveRequestAsync(tenantId, actorUserId, request, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.Failure<RoleDto>(validation.Error);
        }

        using var _ = _db.BeginOperationTenantScope(tenantId);
        var normalizedCode = request.Code.Trim();
        var codeUpper = normalizedCode.ToUpperInvariant();
        var distinctPermissions = request.Permissions.Distinct(StringComparer.Ordinal).ToList();

        try
        {
            return await TenantTransactionSerializer.ExecuteAsync(_db, tenantId, async () =>
            {
                var role = await _db.TenancyRoles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == roleId, cancellationToken);
                if (role is null)
                {
                    return Result.Failure<RoleDto>(new Error("ROLE_NOT_FOUND", "Role was not found in this tenant."));
                }

                if (role.IsSystemBuiltin)
                {
                    return Result.Failure<RoleDto>(new Error("SYSTEM_ROLE_IMMUTABLE", "System built-in roles cannot be modified or weakened."));
                }

                var codeConflict = await _db.TenancyRoles.AnyAsync(
                    r => r.TenantId == tenantId && r.Id != roleId && r.Code.ToUpper() == codeUpper,
                    cancellationToken);
                if (codeConflict)
                {
                    return Result.Failure<RoleDto>(new Error("ROLE_CODE_EXISTS", $"Role code '{normalizedCode}' already exists in this tenant."));
                }

                role.Code = normalizedCode;
                role.Name = request.Name.Trim();
                role.Description = request.Description?.Trim();
                role.IsReadOnlyRole = request.IsReadOnlyRole;

                var existingPerms = await _db.TenancyRolePermissions
                    .Where(p => p.TenantId == tenantId && p.RoleId == role.Id)
                    .ToListAsync(cancellationToken);
                _db.TenancyRolePermissions.RemoveRange(existingPerms);

                foreach (var permission in distinctPermissions)
                {
                    _db.TenancyRolePermissions.Add(new TenancyRolePermission
                    {
                        TenantId = tenantId,
                        RoleId = role.Id,
                        PermissionCode = permission
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);

                return Result.Success(new RoleDto(
                    role.Id,
                    role.Code,
                    role.Name,
                    role.Description,
                    role.IsSystemBuiltin,
                    role.IsReadOnlyRole,
                    distinctPermissions));
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Result.Failure<RoleDto>(new Error("ROLE_CODE_EXISTS", $"Role code '{normalizedCode}' already exists in this tenant."));
        }
    }

    public async Task<Result> DeleteCustomRoleAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var result = await TenantTransactionSerializer.ExecuteAsync(_db, tenantId, async () =>
        {
            var role = await _db.TenancyRoles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == roleId, cancellationToken);
            if (role is null)
            {
                return Result.Failure<bool>(new Error("ROLE_NOT_FOUND", "Role was not found in this tenant."));
            }

            if (role.IsSystemBuiltin)
            {
                return Result.Failure<bool>(new Error("SYSTEM_ROLE_IMMUTABLE", "System built-in roles cannot be deleted."));
            }

            var inUse = await _db.TenancyMemberRoleScopes.AnyAsync(s => s.TenantId == tenantId && s.RoleId == roleId, cancellationToken);
            if (inUse)
            {
                return Result.Failure<bool>(new Error("ROLE_IN_USE", "Remove all member assignments from this role before deleting it."));
            }

            var pendingInvitations = await _db.TenancyInvitations
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.Status == "Pending")
                .Select(i => i.InitialRoleAssignmentsJson)
                .ToListAsync(cancellationToken);

            foreach (var assignmentsJson in pendingInvitations)
            {
                if (InvitationReferencesRole(assignmentsJson, roleId))
                {
                    return Result.Failure<bool>(new Error(
                        "ROLE_IN_USE",
                        "Revoke or remove pending invitations referencing this role before deleting it."));
                }
            }

            _db.TenancyRoles.Remove(role);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(true);
        }, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private async Task<Result> ValidateSaveRequestAsync(
        Guid tenantId,
        Guid actorUserId,
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure(new Error("INVALID_ROLE", "Role code and name are required."));
        }

        var normalizedCode = request.Code.Trim();
        var normalizedName = request.Name.Trim();
        var normalizedDescription = request.Description?.Trim();

        if (normalizedCode.Length > MaxRoleCodeLength || !ValidRoleCodeRegex().IsMatch(normalizedCode))
        {
            return Result.Failure(new Error(
                "INVALID_ROLE",
                "Role code must be 2..64 characters using only letters, digits, '_', '.', or '-'."));
        }

        if (normalizedName.Length > MaxRoleNameLength)
        {
            return Result.Failure(new Error("INVALID_ROLE", $"Role name must not exceed {MaxRoleNameLength} characters."));
        }

        if (normalizedDescription is not null && normalizedDescription.Length > MaxRoleDescriptionLength)
        {
            return Result.Failure(new Error("INVALID_ROLE", $"Role description must not exceed {MaxRoleDescriptionLength} characters."));
        }

        if (ReservedSystemRoleCodes.Contains(normalizedCode))
        {
            return Result.Failure(new Error(
                "SYSTEM_ROLE_CODE_RESERVED",
                $"Role code '{normalizedCode}' is reserved for built-in system roles."));
        }

        if (request.Permissions is null || request.Permissions.Count == 0)
        {
            return Result.Failure(new Error("INVALID_PERMISSIONS", "A role must contain at least one permission."));
        }

        foreach (var permission in request.Permissions)
        {
            if (!ValidCatalog.Contains(permission))
            {
                return Result.Failure(new Error("UNKNOWN_PERMISSION", $"Permission '{permission}' is not in the catalog."));
            }

            if (request.IsReadOnlyRole && !Permissions.IsReadOnlyPermission(permission))
            {
                return Result.Failure(new Error(
                    "READ_ONLY_ROLE_MUTATING_PERMISSION",
                    $"Read-only role cannot include mutating permission '{permission}'."));
            }
        }

        var actorPolicy = await _permissionResolver.ResolveAsync(tenantId, actorUserId, cancellationToken);
        if (actorPolicy is null || actorPolicy.IsReadOnlyRole)
        {
            return Result.Failure(new Error("PRIVILEGE_ESCALATION_DENIED", "Actor has no mutable permissions in this tenant."));
        }

        foreach (var permission in request.Permissions)
        {
            if (!actorPolicy.HasPermission(permission))
            {
                return Result.Failure(new Error(
                    "PRIVILEGE_ESCALATION_DENIED",
                    $"Cannot grant permission '{permission}' because the actor does not hold it."));
            }
        }

        var subscription = await _subscriptionQuery.GetSnapshotAsync(tenantId, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure(new Error("MODULE_NOT_ENTITLED", "Tenant subscription snapshot is unavailable."));
        }

        foreach (var permission in request.Permissions)
        {
            var requiredModule = Permissions.ResolveRequiredModule(permission);
            if (!subscription.ActiveModules.Contains(requiredModule))
            {
                return Result.Failure(new Error(
                    "MODULE_NOT_ENTITLED",
                    $"Permission '{permission}' requires module '{requiredModule}', which is not included in the active plan."));
            }
        }

        return Result.Success();
    }

    private static bool InvitationReferencesRole(string? assignmentsJson, Guid roleId)
    {
        if (string.IsNullOrWhiteSpace(assignmentsJson))
        {
            return false;
        }

        try
        {
            var items = JsonSerializer.Deserialize<RoleScopeAssignmentInput[]>(assignmentsJson);
            return items is not null && items.Any(item => item.RoleId == roleId);
        }
        catch (JsonException)
        {
            // Fail closed if pending invitation payload cannot be parsed cleanly.
            return true;
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
}
