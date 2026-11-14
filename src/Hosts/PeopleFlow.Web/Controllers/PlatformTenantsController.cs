using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/platform/tenants")]
public class PlatformTenantsController : ControllerBase
{
    private readonly IPlatformTenantService _platformTenants;
    private readonly ISubscriptionLifecycleService _subscriptionLifecycle;
    private readonly ICurrentActorContext _actorContext;

    public PlatformTenantsController(
        IPlatformTenantService platformTenants,
        ISubscriptionLifecycleService subscriptionLifecycle,
        ICurrentActorContext actorContext)
    {
        _platformTenants = platformTenants ?? throw new ArgumentNullException(nameof(platformTenants));
        _subscriptionLifecycle = subscriptionLifecycle ?? throw new ArgumentNullException(nameof(subscriptionLifecycle));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
    }

    public sealed record UpdateOverridesRequest(int? OverrideMaxEmployees, long? OverrideMaxStorageBytes);
    public sealed record RecordManualPaymentRequest(decimal Amount, string Currency, string PaymentReference, string Reason);
    public sealed record SuspendTenantRequest(string Reason);

    [HttpGet]
    public async Task<IActionResult> ListTenants(CancellationToken cancellationToken)
    {
        if (!IsVerifiedPlatformAdmin()) return ForbidPlatformAdmin();
        var items = await _platformTenants.ListTenantsAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> ProvisionTenant([FromBody] ProvisionTenantRequest request, CancellationToken cancellationToken)
    {
        if (!IsVerifiedPlatformAdmin()) return ForbidPlatformAdmin();
        var result = await _platformTenants.ProvisionTenantAsync(_actorContext.UserId!.Value, request, cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/platform/tenants/{result.Value.TenantId}", result.Value)
            : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPut("{tenantId:guid}/subscription/overrides")]
    public async Task<IActionResult> UpdateOverrides(Guid tenantId, [FromBody] UpdateOverridesRequest request, CancellationToken cancellationToken)
    {
        if (!IsVerifiedPlatformAdmin()) return ForbidPlatformAdmin();
        var result = await _subscriptionLifecycle.UpdateOverridesAsync(
            tenantId,
            request.OverrideMaxEmployees,
            request.OverrideMaxStorageBytes,
            cancellationToken);
        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPost("{tenantId:guid}/payments/manual")]
    public async Task<IActionResult> RecordManualPayment(Guid tenantId, [FromBody] RecordManualPaymentRequest request, CancellationToken cancellationToken)
    {
        if (!IsVerifiedPlatformAdmin()) return ForbidPlatformAdmin();
        var result = await _subscriptionLifecycle.RecordManualPaymentAsync(
            tenantId,
            request.Amount,
            request.Currency,
            request.PaymentReference,
            _actorContext.UserId!.Value,
            request.Reason,
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPost("{tenantId:guid}/suspend")]
    public async Task<IActionResult> SuspendTenant(Guid tenantId, [FromBody] SuspendTenantRequest request, CancellationToken cancellationToken)
    {
        if (!IsVerifiedPlatformAdmin()) return ForbidPlatformAdmin();
        var result = await _platformTenants.SuspendTenantAsync(_actorContext.UserId!.Value, tenantId, request.Reason, cancellationToken);
        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    [HttpPost("{tenantId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!IsVerifiedPlatformAdmin()) return ForbidPlatformAdmin();
        var result = await _platformTenants.ReactivateTenantAsync(_actorContext.UserId!.Value, tenantId, cancellationToken);
        return result.IsSuccess ? NoContent() : TenantAuthorizationHelper.MapResultError(result.Error);
    }

    private bool IsVerifiedPlatformAdmin()
    {
        if (!_actorContext.UserId.HasValue) return false;
        if (!HttpContext.User.HasClaim("is_platform_admin", "true")) return false;
        var mfaOk = HttpContext.Items.TryGetValue("SessionMfaVerified", out var val) && val is bool sessionMfaVerified
            ? sessionMfaVerified
            : HttpContext.User.HasClaim("mfa_verified", "true");
        return mfaOk;
    }

    private IActionResult ForbidPlatformAdmin() =>
        StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "PLATFORM_ADMIN_REQUIRED",
            Detail = "Verified PlatformAdmin access with completed MFA is required."
        });
}
