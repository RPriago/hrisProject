using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace PeopleFlow.Persistence.Idempotency;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class IdempotencyFilterAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    public bool Required { get; set; } = true;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues))
        {
            if (Required)
            {
                context.Result = CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Idempotency Key Required",
                    "Header 'Idempotency-Key' is required for this endpoint.",
                    "IDEMPOTENCY_KEY_REQUIRED",
                    context.HttpContext);
                return;
            }

            await next();
            return;
        }

        var idempotencyKey = headerValues.ToString().Trim();
        if (idempotencyKey.Length > 128)
        {
            context.Result = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                "Invalid Idempotency Key",
                "Header 'Idempotency-Key' must not exceed 128 characters.",
                "INVALID_IDEMPOTENCY_KEY",
                context.HttpContext);
            return;
        }

        var endpointKey = $"{context.HttpContext.Request.Method}:{context.HttpContext.Request.Path}";
        var idempotencyService = context.HttpContext.RequestServices.GetService<IIdempotencyService>()
            ?? throw new InvalidOperationException(
                "IIdempotencyService is not registered in the request service provider. Fail-closed idempotency enforcement cannot proceed.");

        var bodyRead = await ReadRequestBodyAsync(context.HttpContext.Request, context.HttpContext.RequestAborted);
        if (bodyRead.TooLarge)
        {
            context.Result = CreateProblemResult(
                StatusCodes.Status413PayloadTooLarge,
                "Request Payload Too Large",
                $"Request body exceeds the maximum idempotency buffer limit of {MaxRequestBodyBytes} bytes.",
                "PAYLOAD_TOO_LARGE",
                context.HttpContext);
            return;
        }

        var requestBody = bodyRead.Body;

        var tenantAccessor = context.HttpContext.RequestServices.GetService<PeopleFlow.MultiTenancy.Abstractions.ITenantContextAccessor>();
        var tenantId = tenantAccessor?.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            context.Result = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                "Tenant Context Required",
                "An active tenant context is required to execute idempotent operations.",
                "TENANT_CONTEXT_REQUIRED",
                context.HttpContext);
            return;
        }

        var actorContext = context.HttpContext.RequestServices.GetService<PeopleFlow.Security.Context.ICurrentActorContext>();
        var actorUserId = ExtractActorUserId(context.HttpContext.User) ?? actorContext?.UserId;

        var resolution = await idempotencyService.CheckOrAcquireLockAsync(
            idempotencyKey,
            endpointKey,
            requestBody,
            tenantId: tenantId,
            actorUserId: actorUserId,
            cancellationToken: context.HttpContext.RequestAborted);

        switch (resolution.Status)
        {
            case IdempotencyExecutionStatus.ConflictInProgress:
                context.HttpContext.Response.Headers["Retry-After"] = "2";
                context.Result = CreateProblemResult(
                    StatusCodes.Status409Conflict,
                    "Request In Progress",
                    resolution.ErrorMessage ?? "An identical request with this Idempotency-Key is currently in progress.",
                    "REQUEST_IN_PROGRESS",
                    context.HttpContext);
                return;

            case IdempotencyExecutionStatus.PayloadMismatch:
                context.Result = CreateProblemResult(
                    StatusCodes.Status422UnprocessableEntity,
                    "Idempotency Key Payload Mismatch",
                    resolution.ErrorMessage ?? "The Idempotency-Key has already been used with a different request payload.",
                    "IDEMPOTENCY_KEY_PAYLOAD_MISMATCH",
                    context.HttpContext);
                return;

            case IdempotencyExecutionStatus.CachedResponse:
                context.HttpContext.Response.Headers["X-Cache-Lookup"] = "HIT";
                context.Result = new ContentResult
                {
                    StatusCode = resolution.CachedStatusCode ?? StatusCodes.Status200OK,
                    Content = resolution.CachedResponseBody,
                    ContentType = "application/json"
                };
                return;

            case IdempotencyExecutionStatus.Execute:
            default:
                var executedContext = await next();

                if (executedContext.Exception == null || executedContext.ExceptionHandled)
                {
                    var (statusCode, responseBody) = ExtractResponse(executedContext.Result, context.HttpContext.Response.StatusCode);

                    if (statusCode < StatusCodes.Status500InternalServerError)
                    {
                        await idempotencyService.CompleteAsync(
                            idempotencyKey,
                            endpointKey,
                            statusCode,
                            responseBody,
                            tenantId: tenantId,
                            actorUserId: actorUserId,
                            cancellationToken: context.HttpContext.RequestAborted);
                    }
                    else
                    {
                        await idempotencyService.MarkFailedRetriableAsync(
                            idempotencyKey,
                            endpointKey,
                            tenantId: tenantId,
                            actorUserId: actorUserId,
                            cancellationToken: context.HttpContext.RequestAborted);
                    }
                }
                else
                {
                    await idempotencyService.MarkFailedRetriableAsync(
                        idempotencyKey,
                        endpointKey,
                        tenantId: tenantId,
                        actorUserId: actorUserId,
                        cancellationToken: context.HttpContext.RequestAborted);
                }
                break;
        }
    }

    private const int MaxRequestBodyBytes = 1_048_576; // 1 MB

    private static async Task<(bool TooLarge, string Body)> ReadRequestBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Body == null || !request.Body.CanRead)
        {
            return (false, string.Empty);
        }

        if (request.ContentLength.HasValue && request.ContentLength.Value > MaxRequestBodyBytes)
        {
            return (true, string.Empty);
        }

        request.EnableBuffering(bufferThreshold: 32 * 1024, bufferLimit: MaxRequestBodyBytes);
        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var totalRead = 0;
        int bytesRead;

        while ((bytesRead = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > MaxRequestBodyBytes)
            {
                return (true, string.Empty);
            }

            await ms.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        return (false, Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static Guid? ExtractActorUserId(ClaimsPrincipal? user)
    {
        if (user == null) return null;

        var subClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? user.FindFirst("sub")?.Value;

        if (Guid.TryParse(subClaim, out var userId))
        {
            return userId;
        }

        return null;
    }

    private static (int StatusCode, string? Body) ExtractResponse(IActionResult? result, int responseStatusCode)
    {
        return result switch
        {
            ObjectResult obj => (
                obj.StatusCode ?? responseStatusCode,
                obj.Value is string str ? str : JsonSerializer.Serialize(obj.Value)
            ),
            ContentResult content => (
                content.StatusCode ?? responseStatusCode,
                content.Content
            ),
            StatusCodeResult status => (
                status.StatusCode,
                null
            ),
            _ => (
                responseStatusCode,
                null
            )
        };
    }

    private static ObjectResult CreateProblemResult(
        int statusCode,
        string title,
        string detail,
        string errorCode,
        HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = errorCode;
        problem.Extensions["errorCode"] = errorCode;
        if (!string.IsNullOrWhiteSpace(httpContext.TraceIdentifier))
        {
            problem.Extensions["correlationId"] = httpContext.TraceIdentifier;
        }

        var result = statusCode == StatusCodes.Status400BadRequest
            ? new BadRequestObjectResult(problem)
            : new ObjectResult(problem)
            {
                StatusCode = statusCode
            };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
