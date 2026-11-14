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
[Route("api/v1/settings/subscription")]
public class SettingsSubscriptionController : ControllerBase
{
    private readonly ITenantSubscriptionQueryService _subscriptionQuery;
    private readonly ISubscriptionLifecycleService _subscriptionLifecycle;
    private readonly IActiveHeadcountCounter _headcountCounter;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ICurrentActorContext _actorContext;
    private readonly IAuthorizationGuard _guard;

    public SettingsSubscriptionController(
        ITenantSubscriptionQueryService subscriptionQuery,
        ISubscriptionLifecycleService subscriptionLifecycle,
        IActiveHeadcountCounter headcountCounter,
        IPermissionAndScopeResolver permissionResolver,
        ITenantContextAccessor tenantAccessor,
        ICurrentActorContext actorContext,
        IAuthorizationGuard guard)
    {
        _subscriptionQuery = subscriptionQuery ?? throw new ArgumentNullException(nameof(subscriptionQuery));
        _subscriptionLifecycle = subscriptionLifecycle ?? throw new ArgumentNullException(nameof(subscriptionLifecycle));
        _headcountCounter = headcountCounter ?? throw new ArgumentNullException(nameof(headcountCounter));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _tenantAccessor = tenantAccessor ?? throw new ArgumentNullException(nameof(tenantAccessor));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public sealed record ChangePlanRequest(string TargetPlanCode);

    [HttpGet]
    public async Task<IActionResult> GetCurrentSubscription(CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsSubscriptionRead, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var snap = await _subscriptionQuery.GetSnapshotAsync(_tenantAccessor.TenantId!.Value, cancellationToken);
        return snap is null ? NotFound() : Ok(snap);
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> ListInvoices(CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsSubscriptionRead, isReadOperation: true, cancellationToken);
        if (denied is not null) return denied;

        var invoices = await _subscriptionLifecycle.ListInvoicesAsync(_tenantAccessor.TenantId!.Value, cancellationToken);
        return Ok(invoices);
    }

    [HttpPost("change-plan")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken cancellationToken)
    {
        var denied = await TenantAuthorizationHelper.AuthorizeTenantPermissionAsync(
            HttpContext, _tenantAccessor, _actorContext, _permissionResolver, _subscriptionQuery, _guard,
            Permissions.SettingsSubscriptionManage, isReadOperation: false, cancellationToken);
        if (denied is not null) return denied;

        var tenantId = _tenantAccessor.TenantId!.Value;
        var activeEmployees = await _headcountCounter.CountActiveEmployeesAsync(tenantId, cancellationToken);
        var result = await _subscriptionLifecycle.ChangePlanAsync(
            tenantId,
            request.TargetPlanCode,
            activeEmployees,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }
}
