using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace PeopleFlow.Web.Middleware;

/// <summary>
/// Correlation ID middleware that reads or creates a distributed correlation identifier.
/// Attaches to HttpContext.TraceIdentifier and outgoing X-Correlation-Id response header.
/// </summary>
public partial class CorrelationIdMiddleware
{
    public const string CorrelationIdHeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidCorrelationIdRegex();

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId;

        var incoming = context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var values)
            ? values.FirstOrDefault()
            : null;

        if (!string.IsNullOrEmpty(incoming) && ValidCorrelationIdRegex().IsMatch(incoming))
        {
            correlationId = incoming;
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
