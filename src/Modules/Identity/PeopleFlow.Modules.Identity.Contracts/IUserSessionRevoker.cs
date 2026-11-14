namespace PeopleFlow.Modules.Identity.Contracts;

public interface IUserSessionRevoker
{
    Task<int> RevokeUserTenantSessionsAsync(Guid tenantId, Guid userId, string reason, CancellationToken cancellationToken = default);
    Task<int> RevokeAllUserSessionsAsync(Guid userId, string reason, Guid? exceptSessionId = null, CancellationToken cancellationToken = default);
}
