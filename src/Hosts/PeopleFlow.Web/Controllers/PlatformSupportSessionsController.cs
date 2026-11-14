using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Security.Authorization;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Authorize]
public class PlatformSupportSessionsController : ControllerBase
{
    private readonly ISupportAccessGrantService _supportService;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ICurrentActorContext _actorContext;
    private readonly IAuthorizationGuard _guard;

    public PlatformSupportSessionsController(
        ISupportAccessGrantService supportService,
        IPermissionAndScopeResolver permissionResolver,
        ITenantSubscriptionQueryService subscriptionQuery,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext actorContext,
        IAuthorizationGuard guard)
    {
        _supportService = supportService ?? throw new ArgumentNullException(nameof(supportService));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [HttpGet("api/v1/settings/support-grants")]
    public async Task<IActionResult> ListGrants(CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsSupportGrantsManage, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var items = await _supportService.ListGrantsAsync(_tenantAccessor.TenantId!.Value, cancellationToken);
        return Ok(items);
    }

    [HttpPost("api/v1/settings/support-grants")]
    public async Task<IActionResult> CreateGrant([FromBody] CreateSupportGrantRequest request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsSupportGrantsManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _supportService.CreateGrantAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            request,
            cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/settings/support-grants/{result.Value.Id}", result.Value)
            : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpDelete("api/v1/settings/support-grants/{grantId:guid}")]
    public async Task<IActionResult> RevokeGrant(Guid grantId, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsSupportGrantsManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _supportService.RevokeGrantAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            grantId,
            cancellationToken);

        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpGet("api/v1/settings/audit-logs")]
    public async Task<IActionResult> ListTenantAuditLogs([FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.AuditLogsRead, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var logs = await _supportService.ListTenantAuditLogsAsync(_tenantAccessor.TenantId!.Value, take, cancellationToken);
        return Ok(logs);
    }

    [HttpPost("api/v1/platform/support-sessions/enter")]
    public async Task<IActionResult> EnterSupportSession([FromBody] EnterSupportSessionRequest request, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        if (!HttpContext.User.HasClaim("is_platform_admin", "true"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "PLATFORM_ADMIN_REQUIRED",
                Detail = "PlatformAdmin privileges are required to enter a support session."
            });
        }

        var result = await _supportService.EnterSupportSessionAsync(
            _actorContext.UserId.Value,
            _actorContext.SessionId.Value,
            request.GrantId,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPost("api/v1/platform/support-sessions/exit")]
    public async Task<IActionResult> ExitSupportSession(CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _supportService.ExitSupportSessionAsync(
            _actorContext.UserId.Value,
            _actorContext.SessionId.Value,
            cancellationToken);

        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }
}
