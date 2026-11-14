using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Security.Authorization;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/settings/members")]
public class SettingsMembersController : ControllerBase
{
    private readonly IMemberManagementService _memberManagement;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ICurrentActorContext _actorContext;
    private readonly IAuthorizationGuard _guard;

    public SettingsMembersController(
        IMemberManagementService memberManagement,
        IPermissionAndScopeResolver permissionResolver,
        ITenantSubscriptionQueryService subscriptionQuery,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext actorContext,
        IAuthorizationGuard guard)
    {
        _memberManagement = memberManagement ?? throw new ArgumentNullException(nameof(memberManagement));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [HttpGet]
    public async Task<IActionResult> ListMembers(CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsMembersRead, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var members = await _memberManagement.ListMembersAsync(_tenantAccessor.TenantId!.Value, cancellationToken);
        return Ok(members);
    }

    [HttpPut("{userId:guid}/roles")]
    public async Task<IActionResult> UpdateMemberRoles(Guid userId, [FromBody] UpdateMemberRolesRequest request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsMembersManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _memberManagement.UpdateMemberRolesAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            userId,
            request,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPost("{userId:guid}/revoke")]
    public async Task<IActionResult> RevokeMember(Guid userId, [FromBody] RevokeMemberRequest? request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsMembersManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _memberManagement.RevokeMemberAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            userId,
            request?.Reason ?? "MemberRevoked",
            cancellationToken);

        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }
}
