using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Security.Authorization;

/// <summary>
/// Authenticated user identity and session integrity context (Tier 1).
/// </summary>
public sealed record AuthIdentity(
    Guid? UserId,
    bool IsAuthenticated,
    bool IsActive = true,
    bool IsSessionRevoked = false,
    bool MfaRequired = false,
    bool MfaCompleted = false)
{
    public static readonly AuthIdentity Anonymous = new(null, false);
}

/// <summary>
/// Active tenant membership and lifecycle state context (Tier 2).
/// </summary>
public sealed record TenantIdentity(
    Guid? TenantId,
    bool IsMember = true,
    string SubscriptionStatus = "Active",
    bool IsSupportAccess = false,
    IReadOnlySet<string>? ActiveModules = null)
{
    public static readonly TenantIdentity Empty = new(null, false, "None");
}

/// <summary>
/// Module entitlement and quota requirement context (Tier 3).
/// </summary>
public sealed record ResourceRequirement(
    bool IsReadOperation,
    string? RequiredModule = null,
    string? RequiredPermission = null,
    bool AllowPastDue = false,
    int? CurrentEmployeeCount = null,
    int? MaxEmployeeQuota = null)
{
    public static readonly ResourceRequirement Default = new(IsReadOperation: true);
}

/// <summary>
/// Data scope bounds determining the organizational reach of a granted permission (Tier 4).
/// </summary>
public enum DataScope
{
    Self,
    DirectReports,
    Team,
    Branch,
    LegalEntity,
    Tenant
}

/// <summary>
/// RBAC, fine-grained object scope, and segregation of duties context (Tier 4).
/// </summary>
public sealed record EvaluationSubject(
    Guid? TargetEntityId = null,
    Guid? TargetOwnerId = null,
    bool ProhibitSelfApproval = false,
    IReadOnlySet<string>? ConflictingRoles = null,
    IReadOnlySet<string>? UserRoles = null,
    IReadOnlySet<string>? UserPermissions = null,
    DataScope? GrantedScope = null,
    bool IsTargetWithinScope = true,
    Guid? ActorScopeUnitId = null,
    Guid? TargetScopeUnitId = null,
    bool IsReadOnlyRole = false)
{
    public static readonly EvaluationSubject Empty = new();
}

/// <summary>
/// Composite authorization evaluation context traversing all four server-side security tiers.
/// </summary>
public sealed class AuthorizationContext
{
    public AuthIdentity User { get; set; } = AuthIdentity.Anonymous;
    public TenantIdentity Tenant { get; set; } = TenantIdentity.Empty;
    public ResourceRequirement Requirement { get; set; } = ResourceRequirement.Default;
    public EvaluationSubject Subject { get; set; } = EvaluationSubject.Empty;
}

/// <summary>
/// Server-side 4-tier authorization guard interface.
/// </summary>
public interface IAuthorizationGuard
{
    Task<Result> AuthorizeAsync(AuthorizationContext context, CancellationToken cancellationToken = default);
}
