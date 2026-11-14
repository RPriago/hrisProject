using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Security.Context;
using PeopleFlow.Web.Security;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IIdentityAuthService _authService;
    private readonly ICurrentActorContext _actorContext;

    public AuthController(IIdentityAuthService authService, ICurrentActorContext actorContext)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
    }

    public sealed record LoginRequest(string Email, string Password, string? IncidentReason = null);
    public sealed record MfaVerifyRequest(string Code);
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record ResetPasswordRequest(string Token, string NewPassword);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _authService.LoginAsync(
            request.Email,
            request.Password,
            request.IncidentReason,
            ip,
            userAgent,
            cancellationToken);

        if (result.IsFailure)
        {
            var code = result.Error.Code == "BREAK_GLASS_REASON_REQUIRED"
                ? StatusCodes.Status400BadRequest
                : result.Error.Code == "BREAK_GLASS_MFA_REQUIRED"
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized;

            return StatusCode(code, new ProblemDetails
            {
                Status = code,
                Title = result.Error.Code,
                Detail = result.Error.Message
            });
        }

        await CookieSessionSignInHelper.SignInAsync(HttpContext, result.Value);
        return Ok(new
        {
            status = result.Value.Status.ToString(),
            userId = result.Value.UserId,
            sessionId = result.Value.SessionId,
            activeTenantId = result.Value.ActiveTenantId,
            isPlatformAdmin = result.Value.IsPlatformAdmin,
            isBreakGlass = result.Value.IsBreakGlass,
            mfaVerified = result.Value.MfaVerified
        });
    }

    [HttpPost("mfa/verify")]
    [Authorize]
    [EnableRateLimiting("auth-mfa")]
    public async Task<IActionResult> VerifyMfa([FromBody] MfaVerifyRequest request, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _authService.VerifyMfaAsync(
            _actorContext.UserId.Value,
            _actorContext.SessionId.Value,
            request.Code,
            cancellationToken);

        if (result.IsFailure)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = result.Error.Code,
                Detail = result.Error.Message
            });
        }

        await CookieSessionSignInHelper.SignInAsync(HttpContext, result.Value);
        return Ok(new
        {
            status = result.Value.Status.ToString(),
            userId = result.Value.UserId,
            sessionId = result.Value.SessionId,
            activeTenantId = result.Value.ActiveTenantId,
            isPlatformAdmin = result.Value.IsPlatformAdmin,
            isBreakGlass = result.Value.IsBreakGlass,
            mfaVerified = result.Value.MfaVerified
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (_actorContext.SessionId.HasValue)
        {
            await _authService.LogoutAsync(_actorContext.SessionId.Value, "UserLogout", cancellationToken);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { loggedOut = true });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _authService.ForgotPasswordAsync(request.Email, cancellationToken);
        return Accepted(new { message = "If an account exists for that email, reset instructions have been queued." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = result.Error.Code,
                Detail = result.Error.Message
            });
        }

        return Ok(new { reset = true });
    }
}
