using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Storage.Abstractions;
using PeopleFlow.Storage.Common;

namespace PeopleFlow.Web.Controllers;

/// <summary>
/// Liveness and readiness probe endpoint monitoring PostgreSQL database and S3 storage connectivity.
/// Returns 200 OK when all critical dependencies are healthy, or 503 Service Unavailable on degradation.
/// </summary>
[ApiController]
[Route("healthz")]
public class HealthController : ControllerBase
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly IS3BlobStorage _storage;
    private readonly StorageOptions _storageOptions;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<HealthController>? _logger;

    public HealthController(
        PeopleFlowDbContext dbContext,
        IS3BlobStorage storage,
        StorageOptions? storageOptions = null,
        IHostEnvironment? environment = null,
        ILogger<HealthController>? logger = null)
    {
        _dbContext = dbContext;
        _storage = storage;
        _storageOptions = storageOptions ?? new StorageOptions();
        _environment = environment;
        _logger = logger;
    }

    [HttpGet]
    [EnableRateLimiting("healthz")]
    public async Task<IActionResult> CheckHealth(CancellationToken cancellationToken)
    {
        var dbHealthy = false;
        var storageHealthy = false;
        string? dbError = null;
        string? storageError = null;

        var isDevOrTest = _environment == null || _environment.IsDevelopment() || _environment.IsEnvironment("Testing");

        try
        {
            dbHealthy = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (!dbHealthy)
            {
                dbError = isDevOrTest ? "Database connection probe returned false." : "Database connectivity check failed.";
            }
        }
        catch (Exception ex)
        {
            var summary = PeopleFlow.Security.Redaction.SensitiveDataRedactor.SanitizeExceptionSummary(ex);
            _logger?.LogError("Health check database probe failed: {ExceptionSummary}", summary);
            dbError = isDevOrTest ? summary : "Database connectivity check failed.";
        }

        try
        {
            storageHealthy = _storage != null;
            if (_storage != null)
            {
                var probeBucket = !string.IsNullOrWhiteSpace(_storageOptions.PrivateBucket)
                    ? _storageOptions.PrivateBucket
                    : "peopleflow-private";
                _ = await _storage.ExistsAsync(probeBucket, "probe", cancellationToken);
            }
            else
            {
                storageError = isDevOrTest ? "Storage provider is null or not configured." : "Storage connectivity check failed.";
            }
        }
        catch (Exception ex)
        {
            var summary = PeopleFlow.Security.Redaction.SensitiveDataRedactor.SanitizeExceptionSummary(ex);
            _logger?.LogError("Health check storage probe failed: {ExceptionSummary}", summary);
            storageError = isDevOrTest ? summary : "Storage connectivity check failed.";
            storageHealthy = false;
        }

        var isHealthy = dbHealthy && storageHealthy;
        var payload = new
        {
            status = isHealthy ? "Healthy" : "Unhealthy",
            components = new
            {
                database = new { status = dbHealthy ? "Healthy" : "Unhealthy", error = dbError },
                storage = new { status = storageHealthy ? "Healthy" : "Unhealthy", error = storageError }
            }
        };

        if (isHealthy)
        {
            return Ok(payload);
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }
}
