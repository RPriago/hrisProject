using Microsoft.AspNetCore.Http;

namespace PeopleFlow.Security.Authentication;

/// <summary>
/// Hardened cookie configuration adhering to RFC 6265bis __Host- prefix specifications.
/// </summary>
public static class SessionCookieConfig
{
    public const string CookieName = "__Host-pf_sess";
    public const string DefaultPath = "/";

    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultAbsoluteExpiry = TimeSpan.FromHours(8);

    /// <summary>
    /// Creates hardened cookie options with __Host- restrictions (Secure, HttpOnly, SameSite=Lax, Path=/).
    /// </summary>
    public static CookieOptions CreateCookieOptions(DateTimeOffset? expires = null, TimeSpan? maxAge = null)
    {
        // Sliding expiration is handled in session renewal middleware; default maxAge is set to 8 hours.
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = DefaultPath,
            Expires = expires,
            MaxAge = maxAge ?? DefaultAbsoluteExpiry,
            IsEssential = true
        };
    }
}
