using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PeopleFlow.Web.Filters;

/// <summary>
/// Short-circuits the request with 404 Not Found if the current host environment
/// does not match any of the allowed environments (e.g. Development or Testing).
/// Runs as an IResourceFilter before ActionFilters (e.g. IdempotencyFilter).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class EnvironmentRequiredAttribute : Attribute, IResourceFilter
{
    private readonly string[] _environments;

    public EnvironmentRequiredAttribute(params string[] environments)
    {
        _environments = environments ?? Array.Empty<string>();
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var env = context.HttpContext.RequestServices.GetService<IHostEnvironment>();
        if (env != null && !_environments.Any(e => env.IsEnvironment(e)))
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
