using Microsoft.AspNetCore.Http;

namespace PeopleFlow.Web.Middleware;

/// <summary>
/// Correlation ID middleware that reads or creates a distributed correlation identifier.
/// Attaches to HttpContext.TraceIdentifier and outgoing X-Correlation-Id response header.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string CorrelationIdHeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId;

        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var values) &&
            !string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            correlationId = values.First()!;
        }
        else
        {
            correlationId = Guid.CreateVersion7().ToString();
        }

        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationIdHeaderName] = correlationId;

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationIdHeaderName))
            {
                context.Response.Headers[CorrelationIdHeaderName] = correlationId;
            }
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
