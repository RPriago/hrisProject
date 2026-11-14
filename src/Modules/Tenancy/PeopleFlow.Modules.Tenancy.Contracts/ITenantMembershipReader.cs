namespace PeopleFlow.Modules.Tenancy.Contracts;

public sealed record UserTenantMembershipSummary(
    Guid TenantId,
    string Slug,
    string DisplayName,
    string MembershipStatus,
    bool RequireMfaForAll,
    bool EnforcePayrollSod,
    string TenantStatus,
    bool IsEmergencyAccount);

public sealed record ActiveSupportGrantInfo(
    Guid GrantId,
    Guid TenantId,
    string AccessLevel,
    bool AllowFinancialRead,
    DateTimeOffset ExpiresAtUtc);

public interface ITenantMembershipReader
{
    Task<IReadOnlyList<UserTenantMembershipSummary>> GetActiveMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserTenantMembershipSummary?> GetActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> IsTenantMfaRequiredAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<ActiveSupportGrantInfo?> GetActiveSupportGrantAsync(Guid grantId, Guid adminUserId, CancellationToken cancellationToken = default);
}
