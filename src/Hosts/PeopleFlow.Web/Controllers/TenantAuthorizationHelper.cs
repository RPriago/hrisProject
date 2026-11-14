using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Security.Authorization;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Controllers;

internal static class TenantAuthorizationHelper
{
    public static async Task<IActionResult?> AuthorizeTenantPermissionAsync(
        HttpContext httpContext,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext actorContext,
        IPermissionAndScopeResolver permissionResolver,
        ITenantSubscriptionQueryService subscriptionQuery,
        IAuthorizationGuard guard,
        string requiredPermission,
        bool isReadOperation,
        CancellationToken cancellationToken)
    {
        if (!actorContext.UserId.HasValue)
        {
            return new UnauthorizedObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "AUTH_REQUIRED",
                Detail = "Authentication is required."
            });
        }

        var tenantId = tenantAccessor.TenantId;
        if (!tenantId.HasValue)
        {
            return new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "TENANT_REQUIRED",
                Detail = "An active tenant context is required."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        var isSupport = tenantAccessor.IsSupportAccess || httpContext.User.HasClaim("is_support_access", "true");
        var isApiClient = !isSupport && string.Equals(httpContext.User.FindFirst("auth_mode")?.Value, "api_client", StringComparison.Ordinal);

        EffectiveAccessPolicy? policy = !isSupport && !isApiClient
            ? await permissionResolver.ResolveAsync(tenantId.Value, actorContext.UserId.Value, cancellationToken)
            : null;
        var subscription = await subscriptionQuery.GetSnapshotAsync(tenantId.Value, cancellationToken);

        var mfaRequired = !isApiClient && (httpContext.Items.TryGetValue("SessionMfaRequired", out var reqItem) && reqItem is bool reqBool
            ? reqBool
            : httpContext.User.HasClaim("mfa_required", "true"));
        var mfaCompleted = isApiClient || (httpContext.Items.TryGetValue("SessionMfaVerified", out var mfaItem) && mfaItem is bool verBool
            ? verBool || !mfaRequired
            : httpContext.User.HasClaim("mfa_verified", "true") || !mfaRequired);

        HashSet<string>? effectivePermissions;
        IReadOnlySet<string>? userRoles;
        bool isReadOnlyRole;
        DataScope? grantedScope = null;
        bool isTargetWithinScope = true;

        if (isSupport)
        {
            var allowFinancialRead = httpContext.Items.TryGetValue("SupportAllowFinancialRead", out var finItem) &&
                                     finItem is bool finBool && finBool;
            effectivePermissions = Permissions.AllCatalog
                .Where(p => p.EndsWith(".read", StringComparison.Ordinal) &&
                            (allowFinancialRead || !Permissions.FinancialReadPermissions.Contains(p)))
                .ToHashSet(StringComparer.Ordinal);
            userRoles = new HashSet<string>(StringComparer.Ordinal) { "SupportAdmin" };
            isReadOnlyRole = true;
        }
        else if (isApiClient)
        {
            effectivePermissions = httpContext.User.FindAll("api_scope")
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);
            userRoles = new HashSet<string>(StringComparer.Ordinal) { "ApiClient" };
            isReadOnlyRole = false;

            var scopedLegalEntities = ParseGuidClaims(httpContext.User.FindAll("scoped_legal_entity_id"));
            var scopedLocations = ParseGuidClaims(httpContext.User.FindAll("scoped_location_id"));

            if (scopedLegalEntities.Count > 0 || scopedLocations.Count > 0)
            {
                grantedScope = scopedLocations.Count > 0 ? DataScope.Branch : DataScope.LegalEntity;

                var leOk = scopedLegalEntities.Count == 0 ||
                           (TryGetRequestGuid(httpContext, "legalEntityId", out var reqLeId) &&
                            scopedLegalEntities.Contains(reqLeId));
                var locOk = scopedLocations.Count == 0 ||
                            ((TryGetRequestGuid(httpContext, "locationId", out var reqLocId) ||
                              TryGetRequestGuid(httpContext, "branchId", out reqLocId)) &&
                             scopedLocations.Contains(reqLocId));

                isTargetWithinScope = leOk && locOk;
            }
        }
        else
        {
            effectivePermissions = policy is null
                ? null
                : new HashSet<string>(policy.GrantsByPermission.Keys, StringComparer.Ordinal);
            userRoles = policy?.RoleCodes;
            isReadOnlyRole = policy?.IsReadOnlyRole ?? false;
        }

        var authContext = new AuthorizationContext
        {
            User = new AuthIdentity(
                actorContext.UserId,
                IsAuthenticated: true,
                IsActive: true,
                IsSessionRevoked: false,
                MfaRequired: mfaRequired,
                MfaCompleted: mfaCompleted),
            Tenant = new TenantIdentity(
                tenantId,
                IsMember: policy is not null || isSupport || isApiClient,
                SubscriptionStatus: subscription?.Status ?? string.Empty,
                IsSupportAccess: isSupport,
                ActiveModules: subscription?.ActiveModules),
            Requirement = new ResourceRequirement(
                IsReadOperation: isReadOperation,
                RequiredModule: Permissions.ResolveRequiredModule(requiredPermission),
                RequiredPermission: requiredPermission,
                AllowPastDue: false),
            Subject = new EvaluationSubject(
                UserRoles: userRoles,
                UserPermissions: effectivePermissions,
                GrantedScope: grantedScope,
                IsTargetWithinScope: isTargetWithinScope,
                IsReadOnlyRole: isReadOnlyRole)
        };

        var guardResult = await guard.AuthorizeAsync(authContext, cancellationToken);
        if (!guardResult.IsSuccess)
        {
            return MapError(guardResult.Error, StatusCodes.Status403Forbidden);
        }

        return null;
    }

    public static IActionResult MapResultError(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            _ => error.Code switch
            {
                "ROLE_NOT_FOUND" or "MEMBER_NOT_FOUND" or "INVITATION_NOT_FOUND" or "CLIENT_NOT_FOUND" or "TENANT_NOT_FOUND" or "GRANT_NOT_FOUND" or "PLAN_NOT_FOUND" or "SUBSCRIPTION_NOT_FOUND" => StatusCodes.Status404NotFound,
                "PRIVILEGE_ESCALATION_DENIED" or "PRIVILEGE_ESCALATION_FORBIDDEN" or "MODULE_NOT_ENTITLED" or "SUPPORT_ACCESS_READ_ONLY" or "READ_ONLY_ROLE_MUTATION_DENIED" or "FORBIDDEN" or "SOD_VIOLATION" or "SELF_APPROVAL_PROHIBITED" or "QUOTA_EXCEEDED" or "API_CLIENT_QUOTA_EXCEEDED" or "PLAN_QUOTA_EXCEEDED" or "STORAGE_QUOTA_EXCEEDED" or "TENANT_NOT_ACTIVE" or "TENANT_SUBSCRIPTION_MISSING" or "TENANT_SUBSCRIPTION_INACTIVE" or "EMAIL_MISMATCH" or "USER_INACTIVE" => StatusCodes.Status403Forbidden,
                "INVITATION_AUTH_REQUIRED" => StatusCodes.Status401Unauthorized,
                "ROLE_CODE_EXISTS" or "ROLE_IN_USE" or "ALREADY_MEMBER" or "ALREADY_ACTIVE_MEMBER" or "INVITATION_ALREADY_CONSUMED" or "INVITATION_REVOKED" or "LAST_OWNER_PROTECTED" or "DOWNGRADE_HEADCOUNT_EXCEEDED" or "DOWNGRADE_STORAGE_EXCEEDED" or "DOWNGRADE_API_CLIENT_EXCEEDED" or "PENDING_TRANSACTIONS_EXIST" or "INVALID_STATE_TRANSITION" or "SLUG_EXISTS" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            }
        };
        return MapError(error, status);
    }

    private static HashSet<Guid> ParseGuidClaims(IEnumerable<System.Security.Claims.Claim> claims)
    {
        var set = new HashSet<Guid>();
        foreach (var claim in claims)
        {
            if (Guid.TryParse(claim.Value, out var id) && id != Guid.Empty)
            {
                set.Add(id);
            }
        }
        return set;
    }

    private static bool TryGetRequestGuid(HttpContext httpContext, string key, out Guid value)
    {
        var raw = httpContext.Request.Query[key].FirstOrDefault()
                  ?? httpContext.Request.RouteValues[key]?.ToString();
        return Guid.TryParse(raw, out value) && value != Guid.Empty;
    }

    private static IActionResult MapError(Error error, int status) =>
        new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = error.Code,
            Detail = error.Message
        })
        {
            StatusCode = status
        };
}
