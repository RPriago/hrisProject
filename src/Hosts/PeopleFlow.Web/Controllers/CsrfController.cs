using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeopleFlow.Security.Authentication;
using PeopleFlow.Security.Csrf;

namespace PeopleFlow.Web.Controllers;

/// <summary>
/// Issues session identifiers and HMAC-signed Anti-CSRF tokens for cookie-authenticated browser sessions.
/// </summary>
[ApiController]
[Route("api/v1/auth/csrf")]
public class CsrfController : ControllerBase
{
    private readonly ICsrfTokenService _csrfTokenService;

    public CsrfController(ICsrfTokenService csrfTokenService)
    {
        _csrfTokenService = csrfTokenService;
    }

    [HttpGet]
    [EnableRateLimiting("auth-csrf")]
    public IActionResult GetCsrfToken()
    {
        if (!Request.Cookies.TryGetValue(SessionCookieConfig.CookieName, out var sessionId) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = Guid.CreateVersion7().ToString("N");
            Response.Cookies.Append(
                SessionCookieConfig.CookieName,
                sessionId,
                SessionCookieConfig.CreateCookieOptions());
        }

        var token = _csrfTokenService.GenerateToken(sessionId);

        return Ok(new
        {
            csrfToken = token,
            token = token,
            headerName = "X-CSRF-Token"
        });
    }
}
