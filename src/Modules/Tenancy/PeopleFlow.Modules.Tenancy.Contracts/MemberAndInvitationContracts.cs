using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Modules.Tenancy.Contracts;

public sealed record RoleScopeAssignmentInput(
    Guid RoleId,
    string ScopeType = "Self",
    IReadOnlyCollection<Guid>? ScopeTargetIds = null,
    DateOnly? DataPeriodFrom = null,
    DateOnly? DataPeriodTo = null,
    DateTimeOffset? ValidUntilUtc = null);

public sealed record CreateInvitationRequest(
    string Email,
    Guid? TargetEmployeeId,
    IReadOnlyList<RoleScopeAssignmentInput> RoleAssignments,
    int ExpiresInDays = 7);

public sealed record InvitationCreatedDto(
    Guid InvitationId,
    string InvitedEmail,
    string RawToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record InvitationDto(
    Guid Id,
    string InvitedEmail,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record AcceptInvitationRequest(
    string Token,
    string? NewPassword = null,
    string? PreferredLanguage = null);

public sealed record AcceptInvitationResultDto(
    Guid TenantId,
    Guid MembershipId,
    Guid UserId,
    bool NewAccountCreated);

public sealed record MemberRoleScopeDto(
    Guid Id,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    string ScopeType,
    IReadOnlyList<Guid> ScopeTargetIds,
    DateOnly? DataPeriodFrom,
    DateOnly? DataPeriodTo,
    DateTimeOffset? ValidUntilUtc);

public sealed record TenantMemberDto(
    Guid MembershipId,
    Guid UserId,
    string Status,
    bool IsEmergencyAccount,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    IReadOnlyList<MemberRoleScopeDto> RoleAssignments);

public sealed record UpdateMemberRolesRequest(
    IReadOnlyList<RoleScopeAssignmentInput> RoleAssignments);

public sealed record RevokeMemberRequest(
    string? Reason = null);

public sealed record SwitchTenantRequest(
    Guid TargetTenantId);

public sealed record SwitchTenantResultDto(
    Guid ActiveTenantId,
    string TenantSlug,
    string TenantDisplayName);

public interface IInvitationService
{
    Task<Result<InvitationCreatedDto>> CreateInvitationAsync(
        Guid tenantId,
        Guid actorUserId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvitationDto>> ListInvitationsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<Result<AcceptInvitationResultDto>> AcceptInvitationAsync(
        AcceptInvitationRequest request,
        Guid? authenticatedUserId = null,
        CancellationToken cancellationToken = default);
}

public interface IMemberManagementService
{
    Task<IReadOnlyList<TenantMemberDto>> ListMembersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<Result<TenantMemberDto>> UpdateMemberRolesAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid targetUserId,
        UpdateMemberRolesRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> RevokeMemberAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid targetUserId,
        string reason,
        CancellationToken cancellationToken = default);
}

public interface ITenantSwitcherService
{
    Task<Result<SwitchTenantResultDto>> SwitchActiveTenantAsync(
        Guid userId,
        Guid sessionId,
        Guid targetTenantId,
        CancellationToken cancellationToken = default);
}
