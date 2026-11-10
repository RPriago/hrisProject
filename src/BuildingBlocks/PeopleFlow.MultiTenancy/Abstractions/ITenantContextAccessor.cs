using PeopleFlow.MultiTenancy.Context;

namespace PeopleFlow.MultiTenancy.Abstractions;

/// <summary>
/// Provides access to the verified tenant context for the current execution thread or async context.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// The unique identifier of the active tenant, or null if no tenant is resolved.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// Current subscription status of the active tenant (e.g. Active, Trialing, PastDue, Suspended).
    /// </summary>
    string? SubscriptionStatus { get; }

    /// <summary>
    /// True if the current request is an authorized platform operator accessing under a temporary Support Access Grant.
    /// </summary>
    bool IsSupportAccess { get; }

    /// <summary>
    /// Full immutable tenant context record.
    /// </summary>
    TenantContext CurrentTenant { get; }

    /// <summary>
    /// Sets the active tenant context for the current ambient async execution flow.
    /// </summary>
    void SetTenantContext(TenantContext? context);

    /// <summary>
    /// Clears the ambient tenant context (resets to unauthenticated fail-closed state).
    /// </summary>
    void Clear();

    /// <summary>
    /// Scopes the active tenant context for the lifetime of the returned disposable, restoring the previous context upon disposal.
    /// </summary>
    IDisposable BeginScope(TenantContext context);

    /// <summary>
    /// Scopes the active tenant context for a given tenant identifier.
    /// </summary>
    IDisposable BeginScope(Guid tenantId, string? companySlug = null);
}
