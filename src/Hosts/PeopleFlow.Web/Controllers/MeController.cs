using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Security.Context;
using PeopleFlow.Web.Security;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public class MeController : ControllerBase
{
    private readonly IIdentityAuthService _authService;
    private readonly ICurrentActorContext _actorContext;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ITenantMembershipReader? _membershipReader;
    private readonly IPermissionAndScopeResolver? _permissionResolver;
    private readonly ITenantSubscriptionQueryService? _subscriptionQueryService;

    public MeController(
        IIdentityAuthService authService,
        ICurrentActorContext actorContext,
        ITenantContextAccessor tenantAccessor,
        ITenantMembershipReader? membershipReader = null,
        IPermissionAndScopeResolver? permissionResolver = null,
        ITenantSubscriptionQueryService? subscriptionQueryService = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
        _membershipReader = membershipReader;
        _permissionResolver = permissionResolver;
        _subscriptionQueryService = subscriptionQueryService;
    }

    public sealed record EnableMfaRequest(string Code);
    public sealed record RotateMfaSetupRequest(string CurrentPassword, string CurrentCode);
    public sealed record ConfirmMfaRotationRequest(string Code);
    public sealed record RegenerateRecoveryCodesRequest(string CurrentPassword, string CurrentCode);

    [HttpGet("context")]
    public async Task<IActionResult> GetContext(CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue)
        {
            return Unauthorized();
        }

        var profile = await _authService.GetUserProfileAsync(_actorContext.UserId.Value, cancellationToken);
        if (profile == null)
        {
            return Unauthorized();
        }

        var mfaRequired = HttpContext?.Items.TryGetValue("SessionMfaRequired", out var reqObj) == true && reqObj is bool reqBool
            ? reqBool
            : (profile.TwoFactorEnabled || profile.IsPlatformAdmin || profile.IsBreakGlass);
        var mfaVerified = HttpContext?.Items.TryGetValue("SessionMfaVerified", out var verObj) == true && verObj is bool verBool
            ? verBool
            : (User?.FindFirst("mfa_verified")?.Value == "true" || !mfaRequired);

        // Redact tenant/subscription/RBAC state until session MFA challenge completes.
        if (mfaRequired && !mfaVerified)
        {
            return Ok(new
            {
                user = new
                {
                    profile.UserId,
                    profile.Email,
                    profile.PreferredLanguage,
                    profile.TwoFactorEnabled,
                    profile.IsPlatformAdmin,
                    profile.IsBreakGlass
                },
                mfaRequired = true,
                mfaVerified = false,
                activeTenantId = (Guid?)null,
                isSupportAccess = false,
                availableTenants = Array.Empty<UserTenantMembershipSummary>(),
                subscription = (object?)null,
                roles = Array.Empty<string>(),
                isReadOnlyRole = false,
                permissions = Array.Empty<string>(),
                grants = (object?)null
            });
        }

        var tenants = _membershipReader != null
            ? await _membershipReader.GetActiveMembershipsForUserAsync(profile.UserId, cancellationToken)
            : Array.Empty<UserTenantMembershipSummary>();

        var activeTenantId = _tenantAccessor.TenantId;
        TenantSubscriptionSnapshot? subscription = activeTenantId.HasValue && _subscriptionQueryService != null
            ? await _subscriptionQueryService.GetSnapshotAsync(activeTenantId.Value, cancellationToken)
            : null;

        EffectiveAccessPolicy? policy = activeTenantId.HasValue && _permissionResolver != null && !_tenantAccessor.IsSupportAccess
            ? await _permissionResolver.ResolveAsync(activeTenantId.Value, profile.UserId, cancellationToken)
            : null;

        return Ok(new
        {
            user = new
            {
                profile.UserId,
                profile.Email,
                profile.PreferredLanguage,
                profile.TwoFactorEnabled,
                profile.IsPlatformAdmin,
                profile.IsBreakGlass
            },
            mfaRequired,
            mfaVerified,
            activeTenantId,
            isSupportAccess = _tenantAccessor.IsSupportAccess,
            availableTenants = tenants,
            subscription = subscription == null ? null : new
            {
                subscription.PlanCode,
                subscription.PlanName,
                subscription.Status,
                subscription.IsReadOnlyMode,
                subscription.IsGracePeriodWarning,
                activeModules = subscription.ActiveModules,
                subscription.MaxActiveEmployees,
                subscription.MaxStorageBytes,
                subscription.MaxApiClients
            },
            roles = policy?.RoleCodes ?? _actorContext.Roles,
            isReadOnlyRole = policy?.IsReadOnlyRole ?? _tenantAccessor.IsSupportAccess,
            permissions = policy?.GrantsByPermission.Keys.OrderBy(k => k).ToArray() ?? Array.Empty<string>(),
            grants = policy?.GrantsByPermission.Values
        });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var sessions = await _authService.ListActiveSessionsAsync(_actorContext.UserId.Value, _actorContext.SessionId.Value, cancellationToken);
        return Ok(sessions);
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _authService.RevokeSessionAsync(_actorContext.UserId.Value, id, "UserRevokedSession", cancellationToken);
        if (result.IsFailure)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = result.Error.Code,
                Detail = result.Error.Message
            });
        }

        return Ok(new { revoked = true, sessionId = id });
    }

    [HttpPost("mfa/setup")]
    [EnableRateLimiting("auth-mfa")]
    public async Task<IActionResult> SetupMfa(CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _authService.SetupMfaAsync(_actorContext.UserId.Value, _actorContext.SessionId.Value, cancellationToken);
        if (result.IsFailure)
        {
            return MapMfaError(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost("mfa/enable")]
    [EnableRateLimiting("auth-mfa")]
    public async Task<IActionResult> EnableMfa([FromBody] EnableMfaRequest request, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _authService.EnableMfaAsync(_actorContext.UserId.Value, _actorContext.SessionId.Value, request.Code, cancellationToken);
        if (result.IsFailure)
        {
            return MapMfaError(result.Error);
        }

        await CookieSessionSignInHelper.SignInAsync(HttpContext, result.Value.Session);
        return Ok(new { enabled = true, recoveryCodes = result.Value.RecoveryCodes });
    }

    [HttpPost("mfa/rotate/setup")]
    [EnableRateLimiting("auth-mfa")]
    public async Task<IActionResult> SetupMfaRotation([FromBody] RotateMfaSetupRequest request, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _authService.SetupMfaRotationAsync(
            _actorContext.UserId.Value,
            _actorContext.SessionId.Value,
            request.CurrentPassword,
            request.CurrentCode,
            cancellationToken);
        if (result.IsFailure)
        {
            return MapMfaError(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost("mfa/rotate/confirm")]
    [EnableRateLimiting("auth-mfa")]
    public async Task<IActionResult> ConfirmMfaRotation([FromBody] ConfirmMfaRotationRequest request, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _authService.ConfirmMfaRotationAsync(
            _actorContext.UserId.Value,
            _actorContext.SessionId.Value,
            request.Code,
            cancellationToken);
        if (result.IsFailure)
        {
            return MapMfaError(result.Error);
        }

        await CookieSessionSignInHelper.SignInAsync(HttpContext, result.Value.Session);
        return Ok(new { rotated = true, recoveryCodes = result.Value.RecoveryCodes });
    }

    [HttpPost("mfa/recovery-codes/regenerate")]
    [EnableRateLimiting("auth-mfa")]
    public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] RegenerateRecoveryCodesRequest request, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _authService.RegenerateRecoveryCodesAsync(
            _actorContext.UserId.Value,
            _actorContext.SessionId.Value,
            request.CurrentPassword,
            request.CurrentCode,
            cancellationToken);
        if (result.IsFailure)
        {
            return MapMfaError(result.Error);
        }

        return Ok(new { recoveryCodes = result.Value });
    }

    private static IActionResult MapMfaError(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };

        return new ObjectResult(new ProblemDetails
        {
            Status = statusCode,
            Title = error.Code,
            Detail = error.Message
        })
        {
            StatusCode = statusCode
        };
    }
}
