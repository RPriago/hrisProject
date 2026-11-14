using PeopleFlow.Kernel.Common;
using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Contracts;

public sealed record RoleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystemBuiltin,
    bool IsReadOnlyRole,
    IReadOnlyList<string> Permissions);

public sealed record SaveRoleRequest(
    string Code,
    string Name,
    string? Description,
    bool IsReadOnlyRole,
    IReadOnlyList<string> Permissions);

public interface IRoleManagementService
{
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Result<RoleDto>> CreateCustomRoleAsync(Guid tenantId, Guid actorUserId, SaveRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result<RoleDto>> UpdateRoleAsync(Guid tenantId, Guid actorUserId, Guid roleId, SaveRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteCustomRoleAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken = default);
}

public sealed record SodEvaluationRequest(
    Guid TenantId,
    Guid ActorUserId,
    string OperationType,
    Guid? InitiatedOrCreatedByUserId = null,
    Guid? TargetOwnerUserId = null);

public interface ISodPolicyEvaluator
{
    Task<Result> EvaluateAsync(SodEvaluationRequest request, CancellationToken cancellationToken = default);
}

public interface IOrganizationUnitValidator
{
    Task<bool> AllScopeTargetsExistAsync(Guid tenantId, DataScope scope, IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken = default);
}

public interface IActiveHeadcountCounter
{
    Task<int> CountActiveEmployeesAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface ITenantOffboardingAccessRevoker
{
    Task<Result> RevokeOnOffboardingAsync(Guid tenantId, Guid userId, string reason = "TenantOffboarding", CancellationToken cancellationToken = default);
}
