using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeopleFlow.Jobs.Activator;
using PeopleFlow.Jobs.Configuration;
using PeopleFlow.Jobs.Filters;
using PeopleFlow.Jobs.Services;
using PeopleFlow.Jobs.Tracker;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.MultiTenancy.Interceptors;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Idempotency;
using PeopleFlow.Persistence.Outbox;
using PeopleFlow.Security.Authorization;
using PeopleFlow.Security.Configuration;
using PeopleFlow.Security.Context;
using PeopleFlow.Security.Redaction;
using PeopleFlow.Storage.Abstractions;
using PeopleFlow.Storage.Common;
using PeopleFlow.Storage.CsvValidation;
using PeopleFlow.Storage.Jobs;
using PeopleFlow.Storage.Sanitization;
using PeopleFlow.Storage.Services;
using Serilog.Core;

namespace PeopleFlow.Worker;

public sealed class FailClosedTenantSubscriptionValidator : ITenantSubscriptionValidator
{
    public bool IsTenantExecutionAllowed(Guid tenantId) => false;
    public string? GetSubscriptionStatus(Guid tenantId) => "Suspended";
}

public sealed class DevStubTenantSubscriptionValidator : ITenantSubscriptionValidator
{
    public bool IsTenantExecutionAllowed(Guid tenantId) => tenantId != Guid.Empty;
    public string? GetSubscriptionStatus(Guid tenantId) => "Active";
}

public class HangfireServerHostedService : IHostedService
{
    private BackgroundJobServer? _server;
    private readonly BackgroundJobServerOptions _options;
    private readonly TenantJobActivationFilter _activationFilter;
    private readonly ILogger<HangfireServerHostedService> _logger;

    public HangfireServerHostedService(
        BackgroundJobServerOptions options,
        TenantJobActivationFilter activationFilter,
        ILogger<HangfireServerHostedService> logger)
    {
        _options = options;
        _activationFilter = activationFilter;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!GlobalJobFilters.Filters.Any(f => f.Instance is TenantJobActivationFilter))
        {
            GlobalJobFilters.Filters.Add(_activationFilter);
        }
        _logger.LogInformation("Starting Hangfire server listening on queues: {Queues}", string.Join(", ", _options.Queues));
        _server = new BackgroundJobServer(_options);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Hangfire server.");
        GlobalJobFilters.Filters.Remove(_activationFilter);
        _server?.SendStop();
        _server?.Dispose();
        return Task.CompletedTask;
    }
}

public class Program
{
    public static readonly string[] Queues = new[]
    {
        "default",
        "critical",
        "imports",
        "exports",
        "payroll",
        "notifications"
    };

    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[]? args = null) =>
        Host.CreateDefaultBuilder(args ?? Array.Empty<string>())
            .ConfigureServices((hostContext, services) =>
            {
                StartupSecurityValidator.ValidateHostConfiguration(
                    hostContext.Configuration,
                    hostContext.HostingEnvironment,
                    requireWebSecurityOptions: false);

                var connectionString = hostContext.Configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    if (hostContext.HostingEnvironment.IsDevelopment() || hostContext.HostingEnvironment.IsEnvironment("Testing"))
                    {
                        connectionString = "Host=localhost;Database=peopleflow_dev;Username=peopleflow_app;Password=peopleflow_app_secret";
                    }
                    else
                    {
                        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required in non-development environments.");
                    }
                }

                services.AddSingleton<IClock, SystemClock>();
                services.AddSingleton<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();
                services.AddSingleton<ICurrentActorContext, AsyncLocalCurrentActorContext>();
                services.AddRuntimeLogRedaction();
                services.AddSingleton<IAuthorizationGuard, FourTierAuthorizationPipeline>();
                if (hostContext.HostingEnvironment.IsDevelopment() || hostContext.HostingEnvironment.IsEnvironment("Testing"))
                {
                    services.AddSingleton<ITenantSubscriptionValidator, DevStubTenantSubscriptionValidator>();
                }
                else
                {
                    services.AddSingleton<ITenantSubscriptionValidator, FailClosedTenantSubscriptionValidator>();
                }
                services.AddSingleton<TenantJobActivationFilter>();
                services.TryAddSingleton<IDestructuringPolicy, PiiDestructuringPolicy>();

                var storageOptions = new StorageOptions();
                hostContext.Configuration.GetSection("Storage").Bind(storageOptions);
                services.TryAddSingleton(storageOptions);

                if (storageOptions.UseS3)
                {
                    services.TryAddSingleton<IAmazonS3>(_ =>
                    {
                        var s3Config = new AmazonS3Config
                        {
                            ForcePathStyle = storageOptions.ForcePathStyle
                        };
                        if (!string.IsNullOrWhiteSpace(storageOptions.EndpointUrl))
                        {
                            s3Config.ServiceURL = storageOptions.EndpointUrl;
                        }
                        else if (!string.IsNullOrWhiteSpace(storageOptions.ServiceUrl))
                        {
                            s3Config.ServiceURL = storageOptions.ServiceUrl;
                        }
                        else
                        {
                            s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(storageOptions.Region);
                        }

                        if (!string.IsNullOrWhiteSpace(storageOptions.AccessKey) && !string.IsNullOrWhiteSpace(storageOptions.SecretKey))
                        {
                            var credentials = new BasicAWSCredentials(storageOptions.AccessKey, storageOptions.SecretKey);
                            return new AmazonS3Client(credentials, s3Config);
                        }
                        return new AmazonS3Client(s3Config);
                    });
                    services.TryAddSingleton<IS3BlobStorage, AwsS3BlobStorage>();
                }
                else
                {
                    services.TryAddSingleton<IS3BlobStorage, InMemoryS3BlobStorage>();
                }

                services.AddScoped<IPrivateFileService, S3PrivateFileService>();
                services.AddSingleton<IFileQuarantineScanner>(sp =>
                {
                    var opts = sp.GetRequiredService<StorageOptions>();
                    return opts.UseClamAvDaemon
                        ? new ClamAvDaemonScanner(opts)
                        : new MockOrClamAvScanner();
                });
                services.AddSingleton<ICsvFormulaSanitizer, CsvFormulaSanitizer>();
                services.AddSingleton<IPythonCsvValidationRunner, PythonCsvValidationRunner>();
                services.AddTransient<QuarantineScanJob>();

                services.AddScoped<IIdempotencyService, IdempotencyService>();
                services.AddScoped<IOutboxPublisher, OutboxPublisher>();
                services.AddScoped<IAuditLogWriter, OutboxAuditLogWriter>();
                services.AddScoped<IJobProgressTracker, JobProgressTracker>();
                services.AddSingleton<IBackgroundJobClient>(_ => new BackgroundJobClient(JobStorage.Current));
                services.AddScoped<IBackgroundJobService, HangfireBackgroundJobService>();

                services.AddDbContext<PeopleFlowDbContext>((sp, options) =>
                {
                    var accessor = sp.GetRequiredService<ITenantContextAccessor>();
                    options.UseNpgsql(connectionString)
                           .AddInterceptors(new NpgsqlTenantRlsInterceptor(accessor));
                });

                // Hangfire schema & filters setup
                var postgreSqlOptions = HangfirePostgreSqlOptionsExtensions.CreateDefaultPostgreSqlStorageOptions("hangfire");
                GlobalConfiguration.Configuration
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString), postgreSqlOptions);

                services.AddSingleton(sp => new BackgroundJobServerOptions
                {
                    Queues = Queues,
                    Activator = new ServiceScopeJobActivator(sp.GetRequiredService<IServiceScopeFactory>())
                });
                services.AddHostedService<HangfireServerHostedService>();

                // Register OutboxDispatcherBackgroundService
                services.AddSingleton(new OutboxDispatcherOptions
                {
                    PollingInterval = TimeSpan.FromSeconds(5),
                    BatchSize = 50,
                    MaxRetries = 5,
                    BaseBackoffSeconds = 2.0
                });
                services.AddHostedService<OutboxDispatcherBackgroundService>();
            });
}
