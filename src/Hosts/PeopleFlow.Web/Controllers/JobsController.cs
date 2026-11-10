using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Idempotency;
using PeopleFlow.Web.Filters;

namespace PeopleFlow.Web.Controllers;

[ApiController]
[Route("api/v1/jobs")]
public class JobsController : ControllerBase
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public JobsController(
        PeopleFlowDbContext dbContext,
        ITenantContextAccessor tenantContextAccessor)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContextAccessor = tenantContextAccessor ?? throw new ArgumentNullException(nameof(tenantContextAccessor));
    }

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetJobProgress(Guid jobId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContextAccessor.TenantId ?? _dbContext.CurrentTenantId;
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
    [EnvironmentRequired("Development", "Testing")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [IdempotencyFilter]
    public IActionResult TestIdempotentEndpoint([FromBody] object? payload)
    {
        return Ok(new { success = true });
    }
}
