using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Idempotency;
using PeopleFlow.Security.Authorization;
using PeopleFlow.Security.Context;
using PeopleFlow.Web.Filters;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/jobs")]
public class JobsController : ControllerBase
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ICurrentActorContext _actorContext;
    private readonly IAuthorizationGuard _authorizationGuard;

    public JobsController(
        PeopleFlowDbContext dbContext,
        ITenantContextAccessor tenantContextAccessor,
        ICurrentActorContext actorContext,
        IAuthorizationGuard authorizationGuard)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContextAccessor = tenantContextAccessor ?? throw new ArgumentNullException(nameof(tenantContextAccessor));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _authorizationGuard = authorizationGuard ?? throw new ArgumentNullException(nameof(authorizationGuard));
    }

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetJobProgress(Guid jobId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContextAccessor.TenantId ?? _dbContext.CurrentTenantId;
        var authResult = await _authorizationGuard.AuthorizeAsync(
            new AuthorizationContext
            {
                User = new AuthIdentity(_actorContext.UserId, _actorContext.IsAuthenticated, IsActive: true),
                Tenant = tenantId.HasValue && tenantId.Value != Guid.Empty
                    ? new TenantIdentity(tenantId.Value, IsMember: true, SubscriptionStatus: _tenantContextAccessor.SubscriptionStatus ?? string.Empty)
                    : TenantIdentity.Empty,
                Requirement = new ResourceRequirement(IsReadOperation: true)
            },
            cancellationToken);

        if (authResult.IsFailure)
        {
            var statusCode = authResult.Error.Type == ErrorType.Unauthorized
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden;

            var problem = new ProblemDetails
            {
                Type = $"https://httpstatuses.com/{statusCode}",
                Title = statusCode == StatusCodes.Status401Unauthorized ? "Unauthorized" : "Forbidden",
                Status = statusCode,
                Detail = authResult.Error.Message,
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = authResult.Error.Code;
            problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;

            return new ObjectResult(problem)
            {
                StatusCode = statusCode,
                ContentTypes = { "application/problem+json" }
            };
        }

        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            var problem = new ProblemDetails
            {
                Type = "https://httpstatuses.com/400",
                Title = "Tenant Context Required",
                Status = StatusCodes.Status400BadRequest,
                Detail = "An active tenant context is required to query background jobs.",
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = "TENANT_CONTEXT_REQUIRED";
            problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
            return BadRequest(problem);
        }

        var run = await _dbContext.BackgroundJobRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == jobId && r.TenantId == tenantId.Value, cancellationToken);

        if (run == null)
        {
            var problem = new ProblemDetails
            {
                Type = "https://httpstatuses.com/404",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Job '{jobId}' was not found.",
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = "JOB_NOT_FOUND";
            problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
            return NotFound(problem);
        }

        var isOwner = run.InitiatedByUserId.HasValue &&
                      _actorContext.UserId.HasValue &&
                      run.InitiatedByUserId.Value == _actorContext.UserId.Value;
        var isAdminOrReadAll = _actorContext.Roles.Contains("TenantAdmin", StringComparer.OrdinalIgnoreCase) ||
                               _actorContext.Roles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase) ||
                               _actorContext.Roles.Contains("SystemAdmin", StringComparer.OrdinalIgnoreCase) ||
                               _actorContext.Roles.Contains("HrAdmin", StringComparer.OrdinalIgnoreCase) ||
                               _actorContext.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) ||
                               _actorContext.Roles.Contains("jobs:read_all", StringComparer.OrdinalIgnoreCase) ||
                               User.HasClaim("permission", "jobs:read_all") ||
                               User.HasClaim("permissions", "jobs:read_all");

        if (!isOwner && !isAdminOrReadAll)
        {
            var forbidden = new ProblemDetails
            {
                Type = "https://httpstatuses.com/403",
                Title = "Forbidden",
                Status = StatusCodes.Status403Forbidden,
                Detail = "You do not have permission to view this background job.",
                Instance = HttpContext.Request.Path
            };
            forbidden.Extensions["code"] = "JOB_ACCESS_FORBIDDEN";
            forbidden.Extensions["errorCode"] = "JOB_ACCESS_FORBIDDEN";
            forbidden.Extensions["correlationId"] = HttpContext.TraceIdentifier;
            return StatusCode(StatusCodes.Status403Forbidden, forbidden);
        }

        var processedItems = run.SucceededItems + run.FailedItems;

        return Ok(new
        {
            jobId = run.Id,
            jobCategory = run.JobCategory,
            status = run.Status,
            totalItems = run.TotalItems,
            processedItems = processedItems,
            succeededItems = run.SucceededItems,
            failedItems = run.FailedItems,
            progressPercentage = run.ProgressPercentage,
            errorReportFileId = run.ErrorReportFileId,
            summaryMessage = run.ErrorDetails,
            startedAtUtc = run.StartedAtUtc,
            completedAtUtc = run.CompletedAtUtc
        });
    }

    [HttpPost("test-idempotent")]
    [AllowAnonymous]
    [EnvironmentRequired("Development", "Testing")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [IdempotencyFilter]
    public IActionResult TestIdempotentEndpoint([FromBody] object? payload)
    {
        return Ok(new { success = true });
    }
}
