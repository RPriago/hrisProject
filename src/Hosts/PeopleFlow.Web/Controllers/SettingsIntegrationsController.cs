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
[Route("api/v1/settings/integrations")]
public class SettingsIntegrationsController : ControllerBase
{
    private readonly IApiClientService _clientService;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ICurrentActorContext _actorContext;
    private readonly IAuthorizationGuard _guard;

    public SettingsIntegrationsController(
        IApiClientService clientService,
        IPermissionAndScopeResolver permissionResolver,
        ITenantSubscriptionQueryService subscriptionQuery,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext actorContext,
        IAuthorizationGuard guard)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [HttpGet]
    public async Task<IActionResult> ListClients(CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsIntegrationsManage, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var items = await _clientService.ListClientsAsync(_tenantAccessor.TenantId!.Value, cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateClient([FromBody] CreateApiClientRequest request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsIntegrationsManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _clientService.CreateClientAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            request,
            cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/settings/integrations/{result.Value.Id}", result.Value)
            : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPost("{clientRecordId:guid}/rotate-secret")]
    public async Task<IActionResult> RotateSecret(Guid clientRecordId, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsIntegrationsManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _clientService.RotateSecretAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            clientRecordId,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpDelete("{clientRecordId:guid}")]
    public async Task<IActionResult> RevokeClient(Guid clientRecordId, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsIntegrationsManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var result = await _clientService.RevokeClientAsync(
            _tenantAccessor.TenantId!.Value,
            _actorContext.UserId!.Value,
            clientRecordId,
            cancellationToken);

        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }
}
