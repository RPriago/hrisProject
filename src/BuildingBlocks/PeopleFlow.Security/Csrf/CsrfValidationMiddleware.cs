using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PeopleFlow.Security.Authentication;

namespace PeopleFlow.Security.Csrf;

public sealed class CsrfValidationOptions
{
    public ISet<string> AllowedOrigins { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool AllowSelfOriginFallback { get; set; } = false;
    public bool RequireTokenWhenSessionCookieAbsent { get; set; } = true;
    public string HeaderName { get; set; } = "X-CSRF-Token";
    public string CookieName { get; set; } = SessionCookieConfig.CookieName;
    public ISet<string> ExemptPaths { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class CsrfValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICsrfTokenService _csrfTokenService;
    private readonly CsrfValidationOptions _options;

    public CsrfValidationMiddleware(
        RequestDelegate next,
        ICsrfTokenService csrfTokenService,
        CsrfValidationOptions? options = null)
    {
        _next = next;
        _csrfTokenService = csrfTokenService;
        _options = options ?? new CsrfValidationOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsSafeMethod(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (_options.ExemptPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            await _next(context);
            return;
        }

        // 1. Origin / Referer Verification
        if (!IsOriginAllowed(context))
        {
            await RejectAsync(context, "CSRF_ORIGIN_FORBIDDEN", "Origin or Referer header does not match allowed application hosts.");
            return;
        }

        // 2. Cookie Session Anti-CSRF Token Verification
        var hasSessionCookie = context.Request.Cookies.TryGetValue(_options.CookieName, out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId);

        if (hasSessionCookie)
        {
            if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var tokenValues) ||
                string.IsNullOrWhiteSpace(tokenValues.FirstOrDefault()))
            {
                await RejectAsync(context, "CSRF_TOKEN_MISSING", "Anti-CSRF header 'X-CSRF-Token' is missing for cookie-authenticated mutation.");
                return;
            }

            var token = tokenValues.FirstOrDefault();
            if (!_csrfTokenService.ValidateToken(token, sessionId))
            {
                await RejectAsync(context, "CSRF_TOKEN_INVALID", "Anti-CSRF token verification failed or expired.");
                return;
            }
        }
        else if (_options.RequireTokenWhenSessionCookieAbsent && !IsStatelessBearerAuthenticated(context))
        {
            await RejectAsync(context, "CSRF_TOKEN_MISSING", "Anti-CSRF session cookie and 'X-CSRF-Token' header are required for mutating requests.");
            return;
        }

        await _next(context);
    }

    private static bool IsStatelessBearerAuthenticated(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true &&
        string.Equals(context.User.Identity.AuthenticationType, "Bearer", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeMethod(string method)
    {
        return HttpMethods.IsGet(method) ||
               HttpMethods.IsHead(method) ||
               HttpMethods.IsOptions(method) ||
               HttpMethods.IsTrace(method);
    }

    private bool IsOriginAllowed(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            return MatchesAllowedOrSelf(origin, context);
        }

        var referer = context.Request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(referer))
        {
            return MatchesAllowedOrSelf(referer, context);
        }

        // If neither Origin nor Referer is sent, fail-closed for cookie-authenticated mutations.
        return false;
    }

    private bool MatchesAllowedOrSelf(string rawOriginOrReferer, HttpContext context)
    {
        if (string.Equals(rawOriginOrReferer.Trim(), "null", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(rawOriginOrReferer, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        if (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsedUri.UserInfo))
        {
            return false;
        }

        var normalizedOrigin = $"{parsedUri.Scheme}://{parsedUri.Authority}";
        if (_options.AllowedOrigins.Contains(normalizedOrigin))
        {
            return true;
        }

        if (!_options.AllowSelfOriginFallback)
        {
            return false;
        }

        var requestOrigin = $"{context.Request.Scheme}://{context.Request.Host.Value}";
        return string.Equals(normalizedOrigin, requestOrigin, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RejectAsync(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        var correlationId = context.TraceIdentifier;

        var payload = new
        {
            type = "https://httpstatuses.com/403",
            title = "Forbidden",
            status = StatusCodes.Status403Forbidden,
            code,
            detail = message,
            instance = context.Request.Path.Value,
            correlationId = correlationId
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
