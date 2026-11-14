using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Security.Context;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/auth")]
public class TenancyController : ControllerBase
{
    private readonly ITenantSwitcherService _switcher;
    private readonly ICurrentActorContext _actorContext;

    public TenancyController(ITenantSwitcherService switcher, ICurrentActorContext actorContext)
    {
        _switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
    }

    [HttpPost("switch-active-tenant")]
    public async Task<IActionResult> SwitchActiveTenant([FromBody] SwitchTenantRequest request, CancellationToken cancellationToken)
    {
        if (!_actorContext.UserId.HasValue || !_actorContext.SessionId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _switcher.SwitchActiveTenantAsync(
            _actorContext.UserId.Value,
            _actorContext.SessionId.Value,
            request.TargetTenantId,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : TenantAuthorizationHelper.MapResultError(result.Error);
    }
}
