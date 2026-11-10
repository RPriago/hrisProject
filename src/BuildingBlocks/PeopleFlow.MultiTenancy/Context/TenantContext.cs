namespace PeopleFlow.MultiTenancy.Context;

/// <summary>
/// Immutable context containing active tenant metadata for the current execution context.
/// </summary>
/// <param name="TenantId">Verified identifier of the active tenant, or null if unauthenticated/system.</param>
/// <param name="SubscriptionStatus">Current subscription lifecycle status (e.g. Active, PastDue, Canceled).</param>
/// <param name="IsSupportAccess">True if session is an authorized Platform Admin operating under an active SupportAccessGrant.</param>
/// <param name="TenantCode">Optional business code or slug for the tenant.</param>
public sealed record TenantContext(
    Guid? TenantId,
    string? SubscriptionStatus = null,
    bool IsSupportAccess = false,
    string? TenantCode = null)
{
    public static readonly TenantContext Empty = new(TenantId: null);

    public bool HasTenant => TenantId.HasValue && TenantId.Value != Guid.Empty;
}
