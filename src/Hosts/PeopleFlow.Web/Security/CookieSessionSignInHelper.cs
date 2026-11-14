using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using PeopleFlow.Modules.Identity.Contracts;

namespace PeopleFlow.Web.Security;

internal static class CookieSessionSignInHelper
{
    public static Task SignInAsync(HttpContext httpContext, LoginResultDto login)
    {
        var mfaRequired = !login.MfaVerified ||
                          login.IsPlatformAdmin ||
                          login.IsBreakGlass ||
                          login.Status == LoginOutcomeStatus.MfaChallengeRequired ||
                          login.Status == LoginOutcomeStatus.MfaSetupRequired;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, login.UserId.ToString()),
            new("sid", login.SessionId.ToString()),
            new("auth_mode", "persisted_session"),
            new("is_platform_admin", login.IsPlatformAdmin ? "true" : "false"),
            new("is_break_glass", login.IsBreakGlass ? "true" : "false"),
            new("mfa_verified", login.MfaVerified ? "true" : "false"),
            new("mfa_required", mfaRequired ? "true" : "false")
        };

        if (login.ActiveTenantId.HasValue)
        {
            claims.Add(new Claim("tenant_id", login.ActiveTenantId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = login.IsBreakGlass
                    ? DateTimeOffset.UtcNow.AddMinutes(60)
                    : DateTimeOffset.UtcNow.AddHours(12)
            });
    }
}
