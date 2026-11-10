using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace PeopleFlow.Jobs.Activator;

/// <summary>
/// Hangfire JobActivator backed by Microsoft.Extensions.DependencyInjection.
/// Creates a new IServiceScope per background job execution so scoped services
/// (e.g. DbContext, IPrivateFileService, IJobProgressTracker) are resolved safely and disposed cleanly.
/// </summary>
public sealed class ServiceScopeJobActivator : JobActivator
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public ServiceScopeJobActivator(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    }

    public override object ActivateJob(Type jobType)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        return ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, jobType);
    }

    public override JobActivatorScope BeginScope(JobActivatorContext context)
    {
        return new ServiceScopeJobActivatorScope(_serviceScopeFactory.CreateScope());
    }

#pragma warning disable CS0809
    [Obsolete("Please use BeginScope(JobActivatorContext) instead.")]
    public override JobActivatorScope BeginScope()
    {
        return BeginScope((JobActivatorContext)null!);
    }
#pragma warning restore CS0809

    private sealed class ServiceScopeJobActivatorScope : JobActivatorScope
    {
        private readonly IServiceScope _scope;

        public ServiceScopeJobActivatorScope(IServiceScope scope)
        {
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public override object Resolve(Type type)
        {
            return ActivatorUtilities.GetServiceOrCreateInstance(_scope.ServiceProvider, type);
        }

        public override void DisposeScope()
        {
            _scope.Dispose();
        }
    }
}
