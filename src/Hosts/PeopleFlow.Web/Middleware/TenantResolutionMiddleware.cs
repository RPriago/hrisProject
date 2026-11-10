using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Middleware;

/// <summary>
/// Resolves tenant context from request headers/identity and manages ambient tenant scope.
/// Enforces fail-closed cleanup upon request completion.
/// </summary>
public class TenantResolutionMiddleware
{
    public const string TenantIdHeaderName = "X-Tenant-Id";
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment? _environment;

    public TenantResolutionMiddleware(RequestDelegate next, IHostEnvironment? environment = null)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext? actorContext = null)
    {
        // 1. Resolve tenant from authenticated user claims (all environments including Production)
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var rawClaimTenantId = context.User.FindFirst("tenant_id")?.Value
                ?? context.User.FindFirst("active_tenant_id")?.Value;

            if (Guid.TryParse(rawClaimTenantId, out var claimTenantId) && claimTenantId != Guid.Empty)
            {
                var subscriptionStatus = context.User.FindFirst("subscription_status")?.Value ?? "Active";
                var isSupportAccess = bool.TryParse(context.User.FindFirst("is_support_access")?.Value, out var support) && support;
                tenantAccessor.SetTenantContext(new TenantContext(claimTenantId, subscriptionStatus, isSupportAccess));
            }
        }

        // 2. Gated header fallback for Development and Testing environments
        var isDevOrTest = _environment != null &&
            (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"));

        if (!tenantAccessor.CurrentTenant.HasTenant &&
            isDevOrTest &&
            context.Request.Headers.TryGetValue(TenantIdHeaderName, out var values) &&
            Guid.TryParse(values.FirstOrDefault(), out var tenantId) &&
            tenantId != Guid.Empty)
        {
            tenantAccessor.SetTenantContext(new TenantContext(tenantId, "Active"));
        }

        string? rawUserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(rawUserId) && isDevOrTest &&
            context.Request.Headers.TryGetValue("X-User-Id", out var userHeaderValues))
        {
            rawUserId = userHeaderValues.FirstOrDefault();
        }

        Guid? userId = Guid.TryParse(rawUserId, out var parsedUserId) && parsedUserId != Guid.Empty
            ? parsedUserId
            : null;

        string? rawSessionId = context.User.FindFirst("sid")?.Value
            ?? context.User.FindFirst("session_id")?.Value;

        Guid? sessionId = Guid.TryParse(rawSessionId, out var parsedSessionId) && parsedSessionId != Guid.Empty
            ? parsedSessionId
            : null;

        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Concat(context.User.FindAll("role").Select(c => c.Value));

        var correlationId = Guid.TryParse(context.TraceIdentifier, out var corr) && corr != Guid.Empty
            ? corr
            : Guid.CreateVersion7();

        if (actorContext != null)
        {
            var isAuth = userId.HasValue && userId.Value != Guid.Empty;
            actorContext.SetActorContext(new ActorContext(
                userId,
                sessionId,
                new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase),
                correlationId,
                isAuth));
        }

        try
        {
            await _next(context);
        }
        finally
        {
            tenantAccessor.Clear();
            actorContext?.Clear();
        }
    }
}
