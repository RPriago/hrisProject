using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PeopleFlow.Web.Middleware;

/// <summary>
/// Catches unhandled exceptions and formats standard RFC 7807 application/problem+json responses,
/// strictly omitting internal stack traces to prevent sensitive infrastructure leaks.
/// </summary>
public class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;

    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception caught by ProblemDetailsMiddleware.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var correlationId = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            context.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName] = correlationId;
        }

        var problemDetails = new
        {
            type = "https://httpstatuses.com/500",
            title = "Internal Server Error",
            status = StatusCodes.Status500InternalServerError,
            code = "INTERNAL_SERVER_ERROR",
            detail = "An internal server error occurred while processing your request.",
            instance = context.Request.Path.Value,
            correlationId = correlationId
        };

        var json = JsonSerializer.Serialize(problemDetails);
        await context.Response.WriteAsync(json);
    }
}
