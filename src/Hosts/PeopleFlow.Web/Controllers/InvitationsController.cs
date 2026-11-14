using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Security.Authorization;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Controllers;

[ApiController]
public class InvitationsController : ControllerBase
{
    private readonly IInvitationService _invitationService;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ICurrentActorContext _actorContext;
    private readonly IAuthorizationGuard _guard;

    public InvitationsController(
        IInvitationService invitationService,
        IPermissionAndScopeResolver permissionResolver,
        ITenantSubscriptionQueryService subscriptionQuery,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext actorContext,
        IAuthorizationGuard guard)
    {
        _invitationService = invitationService ?? throw new ArgumentNullException(nameof(invitationService));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [HttpGet("api/v1/settings/invitations")]
    [Authorize]
    public async Task<IActionResult> ListInvitations(CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsMembersRead, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var items = await _invitationService.ListInvitationsAsync(_tenantAccessor.TenantId!.Value, cancellationToken);
        return Ok(items);
    }

    [HttpPost("api/v1/settings/invitations")]
    [Authorize]
    public async Task<IActionResult> CreateInvitation([FromBody] CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsMembersManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _invitationService.CreateInvitationAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            request,
            cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/settings/invitations/{result.Value.InvitationId}", result.Value)
            : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPost("api/v1/invitations/accept")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var result = await _invitationService.AcceptInvitationAsync(request, _actorContext.UserId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }
}
