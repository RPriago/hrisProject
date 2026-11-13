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

        var endpointKey = ComputeEndpointKey(context.HttpContext.Request);
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
        var allowAnonymous = context.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>()
            .Any();

        if (!allowAnonymous && (!actorUserId.HasValue || actorUserId.Value == Guid.Empty))
        {
            context.Result = CreateProblemResult(
                StatusCodes.Status401Unauthorized,
                "Authentication Required",
                "An authenticated actor identity is required for idempotent requests.",
                "AUTH_REQUIRED",
                context.HttpContext);
            return;
        }

        var subStatus = tenantAccessor?.CurrentTenant.SubscriptionStatus;
        if (string.Equals(subStatus, "Suspended", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subStatus, "Terminated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subStatus, "Expired", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = CreateProblemResult(
                StatusCodes.Status403Forbidden,
                "Tenant Suspended",
                "Tenant subscription is not active.",
                "TENANT_SUSPENDED",
                context.HttpContext);
            return;
        }

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
                context.Result = CreateReplayResult(resolution, context.HttpContext);
                return;

            case IdempotencyExecutionStatus.Execute:
            default:
            {
                var executedContext = await next();

                // Persist idempotency completion with a bounded timeout token detached from client disconnects
                using var completionCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                if (executedContext.Exception == null || executedContext.ExceptionHandled)
                {
                    var mvcJsonOptions = context.HttpContext.RequestServices
                        .GetService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
                        ?.Value.JsonSerializerOptions;

                    var (statusCode, responseEnvelopeJson) = ExtractResponse(
                        executedContext.Result,
                        context.HttpContext.Response,
                        mvcJsonOptions);

                    if (statusCode < StatusCodes.Status500InternalServerError)
                    {
                        await idempotencyService.CompleteAsync(
                            idempotencyKey,
                            endpointKey,
                            statusCode,
                            responseEnvelopeJson,
                            tenantId: tenantId,
                            actorUserId: actorUserId,
                            cancellationToken: completionCts.Token);
                    }
                    else
                    {
                        await idempotencyService.MarkFailedRetriableAsync(
                            idempotencyKey,
                            endpointKey,
                            tenantId: tenantId,
                            actorUserId: actorUserId,
                            cancellationToken: completionCts.Token);
                    }
                }
                else
                {
                    await idempotencyService.MarkFailedRetriableAsync(
                        idempotencyKey,
                        endpointKey,
                        tenantId: tenantId,
                        actorUserId: actorUserId,
                        cancellationToken: completionCts.Token);
                }
                break;
            }
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

    private sealed record IdempotencyResponseEnvelope(
        bool EnvelopeMarker,
        int StatusCode,
        string? ContentType,
        string? Body,
        string? Location);

    private static string ComputeEndpointKey(HttpRequest request)
    {
        var method = request.Method.ToUpperInvariant();
        var path = request.Path.Value ?? "/";

        string canonical;
        if (!request.QueryString.HasValue)
        {
            canonical = $"{method}:{path}";
        }
        else
        {
            var parsed = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.QueryString.Value);
            var queryParts = parsed
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .SelectMany(kv => kv.Value
                    .Where(v => v != null)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .Select(v => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v!)}"));
            canonical = $"{method}:{path}?{string.Join('&', queryParts)}";
        }

        if (canonical.Length <= 160)
        {
            return canonical;
        }

        var hashBytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"{canonical[..95]}#{hashHex}";
    }

    private static (int StatusCode, string BodyEnvelopeJson) ExtractResponse(
        IActionResult? result,
        HttpResponse response,
        JsonSerializerOptions? jsonOptions)
    {
        var serializerOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var location = response.Headers.Location.ToString();
        if (string.IsNullOrWhiteSpace(location))
        {
            location = null;
        }

        int statusCode;
        string? contentType;
        string? body;

        switch (result)
        {
            case JsonResult jsonRes:
                statusCode = jsonRes.StatusCode ?? response.StatusCode;
                contentType = string.IsNullOrWhiteSpace(jsonRes.ContentType) ? "application/json" : jsonRes.ContentType;
                body = jsonRes.Value is null
                    ? null
                    : JsonSerializer.Serialize(jsonRes.Value, jsonRes.SerializerSettings as JsonSerializerOptions ?? serializerOptions);
                break;

            case ObjectResult obj:
                statusCode = obj.StatusCode ?? response.StatusCode;
                if (obj.Value is string strVal)
                {
                    contentType = obj.ContentTypes.FirstOrDefault() ?? "text/plain";
                    body = strVal;
                }
                else
                {
                    contentType = obj.ContentTypes.FirstOrDefault() ?? "application/json";
                    body = obj.Value is null ? null : JsonSerializer.Serialize(obj.Value, serializerOptions);
                }
                break;

            case ContentResult content:
                statusCode = content.StatusCode ?? response.StatusCode;
                contentType = string.IsNullOrWhiteSpace(content.ContentType) ? "text/plain" : content.ContentType;
                body = content.Content;
                break;

            case StatusCodeResult status:
                statusCode = status.StatusCode;
                contentType = null;
                body = null;
                break;

            default:
                statusCode = response.StatusCode;
                contentType = null;
                body = null;
                break;
        }

        var envelope = new IdempotencyResponseEnvelope(
            EnvelopeMarker: true,
            StatusCode: statusCode,
            ContentType: contentType,
            Body: body,
            Location: location);

        return (statusCode, JsonSerializer.Serialize(envelope));
    }

    private static IActionResult CreateReplayResult(IdempotencyResolution resolution, HttpContext httpContext)
    {
        var fallbackStatus = resolution.CachedStatusCode ?? StatusCodes.Status200OK;
        var raw = resolution.CachedResponseBody;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("EnvelopeMarker", out var marker) &&
                    marker.ValueKind == JsonValueKind.True)
                {
                    var envelope = doc.RootElement.Deserialize<IdempotencyResponseEnvelope>();
                    if (envelope != null)
                    {
                        if (!string.IsNullOrWhiteSpace(envelope.Location))
                        {
                            httpContext.Response.Headers["Location"] = envelope.Location;
                        }

                        var status = envelope.StatusCode > 0 ? envelope.StatusCode : fallbackStatus;
                        if (status == StatusCodes.Status204NoContent ||
                            (envelope.Body == null && envelope.ContentType == null))
                        {
                            return new StatusCodeResult(status);
                        }

                        return new ContentResult
                        {
                            StatusCode = status,
                            Content = envelope.Body,
                            ContentType = envelope.ContentType ?? "application/json"
                        };
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to direct payload replay below
            }
        }

        if (fallbackStatus == StatusCodes.Status204NoContent)
        {
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        return new ContentResult
        {
            StatusCode = fallbackStatus,
            Content = raw,
            ContentType = "application/json"
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
