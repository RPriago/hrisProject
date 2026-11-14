using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Middleware;

/// <summary>
/// Resolves tenant and actor context from persisted sessions, API clients, or dev/test fixtures.
/// Enforces live session revocation, MFA completion, Support Access boundaries, and fail-closed cleanup.
/// </summary>
public class TenantResolutionMiddleware
{
    public const string TenantIdHeaderName = "X-Tenant-Id";

    private static readonly string[] MfaPendingAlwaysAllowedPaths =
    [
        "/api/v1/auth/mfa/verify",
        "/api/v1/auth/logout",
        "/api/v1/auth/csrf",
        "/api/v1/me/context",
        "/healthz"
    ];

    private static readonly string[] MfaPendingEnrollmentOnlyPaths =
    [
        "/api/v1/me/mfa/setup",
        "/api/v1/me/mfa/enable"
    ];

    private static readonly string[] MembershipExemptPrefixes =
    [
        "/api/v1/auth/switch-active-tenant",
        "/api/v1/auth/logout",
        "/api/v1/auth/csrf",
        "/api/v1/me/",
        "/api/v1/invitations/accept"
    ];

    private static readonly string[] FinancialReadPrefixes =
    [
        "/api/v1/payroll",
        "/api/v1/settings/subscription",
        "/api/v1/analytics"
    ];

    private static readonly string[] SupportExitOrLogoutPrefixes =
    [
        "/api/v1/platform/support-sessions/exit",
        "/api/v1/auth/logout"
    ];

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment? _environment;
    private readonly IConfiguration? _configuration;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        IHostEnvironment? environment = null,
        IConfiguration? configuration = null)
    {
        _next = next;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext? actorContext = null)
    {
        var isAuthenticatedPrincipal = context.User.Identity?.IsAuthenticated == true;
        var authMode = context.User.FindFirst("auth_mode")?.Value;

        string? rawUserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        string? rawSessionId = context.User.FindFirst("sid")?.Value
            ?? context.User.FindFirst("session_id")?.Value;

        Guid? userId = Guid.TryParse(rawUserId, out var parsedUserId) && parsedUserId != Guid.Empty ? parsedUserId : null;
        Guid? sessionId = Guid.TryParse(rawSessionId, out var parsedSessionId) && parsedSessionId != Guid.Empty ? parsedSessionId : null;

        var roles = new HashSet<string>(
            context.User.FindAll(ClaimTypes.Role).Select(c => c.Value)
                .Concat(context.User.FindAll("role").Select(c => c.Value)),
            StringComparer.OrdinalIgnoreCase);

        var correlationId = Guid.TryParse(context.TraceIdentifier, out var corr) && corr != Guid.Empty
            ? corr
            : Guid.CreateVersion7();

        try
        {
            if (isAuthenticatedPrincipal &&
                string.Equals(authMode, "persisted_session", StringComparison.Ordinal))
            {
                var sessionResolver = context.RequestServices.GetService<ISessionContextResolver>();
                if (sessionResolver is not null)
                {
                    var membershipReader = context.RequestServices.GetService<ITenantMembershipReader>();
                    var subscriptionQuery = context.RequestServices.GetService<ITenantSubscriptionQueryService>();
                    var platformAudit = context.RequestServices.GetService<IPlatformAuditLogWriter>();

                    if (!sessionId.HasValue)
                    {
                        await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "SESSION_INVALID", "Session identifier is missing.");
                        return;
                    }

                    var session = await sessionResolver.ResolveActiveSessionAsync(sessionId.Value, context.RequestAborted);
                    if (session is null || (userId.HasValue && session.UserId != userId.Value))
                    {
                        await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "SESSION_REVOKED_OR_EXPIRED", "Your session has expired or been revoked.");
                        return;
                    }

                    userId = session.UserId;
                    var path = context.Request.Path.Value ?? string.Empty;
                    var mfaRequired = session.IsPlatformAdmin || session.TwoFactorEnabled || session.IsBreakGlass;
                    context.Items["SessionMfaVerified"] = session.IsMfaVerified;
                    context.Items["SessionMfaRequired"] = mfaRequired;

                    if (session.IsPlatformAdmin)
                    {
                        roles.Add("PlatformAdmin");
                    }

                    if (mfaRequired && !session.IsMfaVerified && !IsAllowedDuringPendingMfa(path, session))
                    {
                        await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "MFA_VERIFICATION_REQUIRED", "Multi-factor authentication verification is required.");
                        return;
                    }

                    if (session.SupportGrantId.HasValue && membershipReader is not null)
                    {
                        var grant = await membershipReader.GetActiveSupportGrantAsync(session.SupportGrantId.Value, session.UserId, context.RequestAborted);
                        if (grant is null)
                        {
                            await sessionResolver.SetSupportGrantAsync(session.SessionId, null, null, context.RequestAborted);
                            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "SUPPORT_GRANT_EXPIRED_OR_REVOKED", "Support access grant is expired or revoked.");
                            return;
                        }

                        var isSafeHttpMethod = HttpMethods.IsGet(context.Request.Method) ||
                                               HttpMethods.IsHead(context.Request.Method) ||
                                               HttpMethods.IsOptions(context.Request.Method);
                        var isExitOrLogout = StartsWithAny(path, SupportExitOrLogoutPrefixes);

                        if (!isSafeHttpMethod && !isExitOrLogout)
                        {
                            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "SUPPORT_ACCESS_READ_ONLY", "Support access sessions are strictly read-only.");
                            return;
                        }

                        context.Items["SupportAllowFinancialRead"] = grant.AllowFinancialRead;

                        if (!grant.AllowFinancialRead && StartsWithAny(path, FinancialReadPrefixes))
                        {
                            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "SUPPORT_FINANCIAL_READ_DENIED", "Financial read access was not authorized on this support grant.");
                            return;
                        }

                        var snap = subscriptionQuery is not null
                            ? await subscriptionQuery.GetSnapshotAsync(grant.TenantId, context.RequestAborted)
                            : null;
                        tenantAccessor.SetTenantContext(new TenantContext(grant.TenantId, snap?.Status ?? string.Empty, true));

                        actorContext?.SetActorContext(new ActorContext(userId, sessionId, roles, correlationId, true));
                        if (platformAudit is not null)
                        {
                            await platformAudit.WriteAsync(new PlatformAuditEntry(
                                userId,
                                "PlatformAdmin",
                                "SUPPORT_SESSION_REQUEST",
                                TargetTenantId: grant.TenantId,
                                SupportGrantId: grant.GrantId,
                                ResourcePath: $"{context.Request.Method} {path}",
                                CorrelationId: correlationId), context.RequestAborted);
                        }
                    }
                    else if (session.ActiveTenantId.HasValue)
                    {
                        if (membershipReader is not null)
                        {
                            var membership = await membershipReader.GetActiveMembershipAsync(session.ActiveTenantId.Value, session.UserId, context.RequestAborted);
                            if (membership is null)
                            {
                                if (!StartsWithAny(path, MembershipExemptPrefixes))
                                {
                                    await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "TENANT_MEMBERSHIP_REVOKED", "Your membership in this tenant is no longer active.");
                                    return;
                                }
                            }
                            else if (membership.RequireMfaForAll)
                            {
                                mfaRequired = true;
                                context.Items["SessionMfaRequired"] = true;
                                if (!session.IsMfaVerified && !IsAllowedDuringPendingMfa(path, session))
                                {
                                    await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "MFA_VERIFICATION_REQUIRED", "This tenant requires MFA verification on the active session.");
                                    return;
                                }
                            }
                        }

                        if (!mfaRequired || session.IsMfaVerified)
                        {
                            var snap = subscriptionQuery is not null
                                ? await subscriptionQuery.GetSnapshotAsync(session.ActiveTenantId.Value, context.RequestAborted)
                                : null;
                            tenantAccessor.SetTenantContext(new TenantContext(session.ActiveTenantId.Value, snap?.Status ?? string.Empty, false));
                        }
                    }
                }
            }
            else if (isAuthenticatedPrincipal && string.Equals(authMode, "api_client", StringComparison.Ordinal))
            {
                var subscriptionQuery = context.RequestServices.GetService<ITenantSubscriptionQueryService>();
                var rawClaimTenantId = context.User.FindFirst("tenant_id")?.Value
                    ?? context.User.FindFirst("active_tenant_id")?.Value;
                if (!Guid.TryParse(rawClaimTenantId, out var claimTenantId) || claimTenantId == Guid.Empty)
                {
                    await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "API_CLIENT_INVALID", "API client tenant context is missing or invalid.");
                    return;
                }

                var snap = subscriptionQuery is not null
                    ? await subscriptionQuery.GetSnapshotAsync(claimTenantId, context.RequestAborted)
                    : null;
                if (snap is null ||
                    (!string.Equals(snap.Status, SubscriptionStatusConstants.Active, StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(snap.Status, SubscriptionStatusConstants.Trial, StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(snap.Status, SubscriptionStatusConstants.PastDue, StringComparison.OrdinalIgnoreCase)))
                {
                    await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "SUBSCRIPTION_SUSPENDED", "Tenant subscription is not active.");
                    return;
                }

                var isSafeMethod = HttpMethods.IsGet(context.Request.Method) ||
                                   HttpMethods.IsHead(context.Request.Method) ||
                                   HttpMethods.IsOptions(context.Request.Method);
                if (string.Equals(snap.Status, SubscriptionStatusConstants.PastDue, StringComparison.OrdinalIgnoreCase) && !isSafeMethod)
                {
                    await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "TENANT_READ_ONLY_PAST_DUE", "Tenant account is past due. Mutating API client requests are blocked.");
                    return;
                }

                tenantAccessor.SetTenantContext(new TenantContext(claimTenantId, snap.Status, false));
            }
            else if (isAuthenticatedPrincipal)
            {
                var isDevOrTest = _environment != null &&
                    (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"));
                if (!isDevOrTest)
                {
                    await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "AUTH_SCHEME_UNSUPPORTED", "Authentication scheme is not supported in this environment.");
                    return;
                }

                // Preserved path for Modul 01A mock cookie test schemes without persisted_session auth_mode in Dev/Testing only.
                var rawClaimTenantId = context.User.FindFirst("tenant_id")?.Value
                    ?? context.User.FindFirst("active_tenant_id")?.Value;

                if (Guid.TryParse(rawClaimTenantId, out var claimTenantId) && claimTenantId != Guid.Empty)
                {
                    var subscriptionStatus = context.User.FindFirst("subscription_status")?.Value ?? string.Empty;
                    var isSupportAccess = bool.TryParse(context.User.FindFirst("is_support_access")?.Value, out var support) && support;
                    tenantAccessor.SetTenantContext(new TenantContext(claimTenantId, subscriptionStatus, isSupportAccess));
                }
            }
            else
            {
                var isDevOrTest = _environment != null &&
                    (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"));
                var allowHeaderOverride = isDevOrTest &&
                    _configuration?.GetValue<bool>("Security:AllowHeaderContextOverride") == true;

                if (!tenantAccessor.CurrentTenant.HasTenant &&
                    allowHeaderOverride &&
                    context.Request.Headers.TryGetValue(TenantIdHeaderName, out var values) &&
                    Guid.TryParse(values.FirstOrDefault(), out var tenantId) &&
                    tenantId != Guid.Empty)
                {
                    tenantAccessor.SetTenantContext(new TenantContext(tenantId, "Active"));
                }

                if (string.IsNullOrWhiteSpace(rawUserId) &&
                    allowHeaderOverride &&
                    context.Request.Headers.TryGetValue("X-User-Id", out var userHeaderValues))
                {
                    rawUserId = userHeaderValues.FirstOrDefault();
                    userId = Guid.TryParse(rawUserId, out var hdrUserId) && hdrUserId != Guid.Empty ? hdrUserId : null;
                }
            }

            if (actorContext != null)
            {
                var isAuth = isAuthenticatedPrincipal && userId.HasValue && userId.Value != Guid.Empty;
                actorContext.SetActorContext(new ActorContext(userId, sessionId, roles, correlationId, isAuth));
            }

            await _next(context);
        }
        finally
        {
            tenantAccessor.Clear();
            actorContext?.Clear();
        }
    }

    private static bool IsAllowedDuringPendingMfa(string path, ResolvedSessionInfo session)
    {
        if (MatchesAnyExactPath(path, MfaPendingAlwaysAllowedPaths))
        {
            return true;
        }

        return !session.TwoFactorEnabled && MatchesAnyExactPath(path, MfaPendingEnrollmentOnlyPaths);
    }

    private static bool MatchesAnyExactPath(string path, string[] allowedPaths)
    {
        var cleanPath = path.Length > 1 ? path.TrimEnd('/') : path;
        foreach (var candidate in allowedPaths)
        {
            if (cleanPath.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool StartsWithAny(string path, string[] prefixes)
    {
        var cleanPath = path.Length > 1 ? path.TrimEnd('/') : path;
        foreach (var prefix in prefixes)
        {
            var cleanPrefix = prefix.Length > 1 ? prefix.TrimEnd('/') : prefix;
            if (cleanPath.Equals(cleanPrefix, StringComparison.OrdinalIgnoreCase) ||
                cleanPath.StartsWith(cleanPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string code, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new ProblemDetails
        {
            Status = statusCode,
            Title = code,
            Detail = detail
        }));
    }
}
