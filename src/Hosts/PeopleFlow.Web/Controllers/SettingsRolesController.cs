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
[Route("api/v1/settings/roles")]
public class SettingsRolesController : ControllerBase
{
    private readonly IRoleManagementService _roleManagement;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ICurrentActorContext _actorContext;
    private readonly IAuthorizationGuard _guard;

    public SettingsRolesController(
        IRoleManagementService roleManagement,
        IPermissionAndScopeResolver permissionResolver,
        ITenantSubscriptionQueryService subscriptionQuery,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext actorContext,
        IAuthorizationGuard guard)
    {
        _roleManagement = roleManagement ?? throw new ArgumentNullException(nameof(roleManagement));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [HttpGet]
    public async Task<IActionResult> ListRoles(CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsRolesRead, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var roles = await _roleManagement.ListRolesAsync(_tenantAccessor.TenantId!.Value, cancellationToken);
        return Ok(roles);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsRolesManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _roleManagement.CreateCustomRoleAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            request,
            cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/settings/roles/{result.Value.Id}", result.Value)
            : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPut("{roleId:guid}")]
    public async Task<IActionResult> UpdateRole(Guid roleId, [FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsRolesManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _roleManagement.UpdateRoleAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            roleId,
            request,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpDelete("{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid roleId, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsRolesManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _roleManagement.DeleteCustomRoleAsync(_tenantAccessor.TenantId!.Value, roleId, cancellationToken);
        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }
}
