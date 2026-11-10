using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.States;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PeopleFlow.Jobs.Configuration;
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
using PeopleFlow.Security.Context;
using PeopleFlow.Security.Csrf;
using PeopleFlow.Security.Redaction;
using PeopleFlow.Storage.Abstractions;
using PeopleFlow.Storage.Common;
using PeopleFlow.Storage.CsvValidation;
using PeopleFlow.Storage.Jobs;
using PeopleFlow.Storage.Sanitization;
using PeopleFlow.Storage.Services;
using PeopleFlow.Web.Middleware;
using Serilog.Core;

namespace PeopleFlow.Web;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = CreateWebApplicationBuilder(args);
        var app = builder.Build();
        ConfigurePipeline(app);
        app.Run();
    }

    public static WebApplicationBuilder CreateWebApplicationBuilder(
        string[]? args = null,
        Action<IServiceCollection>? configureServices = null,
        string? environmentName = null)
    {
        var options = new WebApplicationOptions
        {
            Args = args,
            EnvironmentName = environmentName
        };
        var builder = WebApplication.CreateBuilder(options);

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(Program).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddDataProtection();

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();
        builder.Services.AddSingleton<ICurrentActorContext, AsyncLocalCurrentActorContext>();
        builder.Services.AddSingleton<IAuthorizationGuard, FourTierAuthorizationPipeline>();
        builder.Services.TryAddSingleton<IDestructuringPolicy, PiiDestructuringPolicy>();

        var csrfOptions = new CsrfTokenOptions();
        builder.Configuration.GetSection("Security:Csrf").Bind(csrfOptions);
        builder.Services.TryAddSingleton(csrfOptions);
        builder.Services.TryAddSingleton<ICsrfTokenService>(sp => new CsrfTokenService(sp.GetRequiredService<CsrfTokenOptions>()));

        var allowedOrigins = builder.Configuration.GetSection("Security:Csrf:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        var csrfValidationOptions = new CsrfValidationOptions
        {
            AllowedOrigins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase),
            ExemptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "/api/v1/auth/csrf",
                "/healthz",
                "/api/v1/test/unhandled-error"
            }
        };
        builder.Services.AddSingleton(csrfValidationOptions);

        var storageOptions = new StorageOptions();
        builder.Configuration.GetSection("Storage").Bind(storageOptions);
        builder.Services.TryAddSingleton(storageOptions);

        if (storageOptions.UseS3)
        {
            builder.Services.TryAddSingleton<IAmazonS3>(_ =>
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
            builder.Services.TryAddSingleton<IS3BlobStorage, AwsS3BlobStorage>();
        }
        else
        {
            builder.Services.TryAddSingleton<IS3BlobStorage, InMemoryS3BlobStorage>();
        }

        builder.Services.AddScoped<IPrivateFileService, S3PrivateFileService>();
        builder.Services.AddSingleton<IFileQuarantineScanner, MockOrClamAvScanner>();
        builder.Services.AddSingleton<ICsvFormulaSanitizer, CsvFormulaSanitizer>();
        builder.Services.AddSingleton<IPythonCsvValidationRunner, PythonCsvValidationRunner>();
        builder.Services.AddTransient<QuarantineScanJob>();

        builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
        builder.Services.AddScoped<IOutboxPublisher, OutboxPublisher>();
        builder.Services.AddScoped<IAuditLogWriter, OutboxAuditLogWriter>();
        builder.Services.AddScoped<IJobProgressTracker, JobProgressTracker>();

        if (configureServices != null)
        {
            configureServices(builder.Services);
        }
        else
        {
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Database=peopleflow_dev;Username=peopleflow_app;Password=peopleflow_app_secret";

            builder.Services.AddDbContext<PeopleFlowDbContext>((sp, options) =>
            {
                var accessor = sp.GetRequiredService<ITenantContextAccessor>();
                options.UseNpgsql(connectionString)
                       .AddInterceptors(new NpgsqlTenantRlsInterceptor(accessor));
            });

            var postgreSqlOptions = HangfirePostgreSqlOptionsExtensions.CreateDefaultPostgreSqlStorageOptions("hangfire");
            GlobalConfiguration.Configuration
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString), postgreSqlOptions);
        }

        builder.Services.AddSingleton<IBackgroundJobClient>(_ =>
        {
            try
            {
                return new BackgroundJobClient(JobStorage.Current);
            }
            catch
            {
                return new DefaultFallbackBackgroundJobClient();
            }
        });
        builder.Services.AddScoped<IBackgroundJobService, HangfireBackgroundJobService>();

        return builder;
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        // Enforce pipeline order: CorrelationId -> ProblemDetails -> Csrf -> TenantResolution -> Routing / Controllers
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ProblemDetailsMiddleware>();
        app.UseMiddleware<CsrfValidationMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();

        app.UseRouting();

        // Guard test-only simulation routes so they are never exposed in Production
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.MapGet("/api/v1/test/unhandled-error", () =>
            {
                throw new InvalidOperationException("Simulated catastrophic crash in controller pipeline.");
            });

            app.MapGet("/api/v1/test/current-tenant", (ITenantContextAccessor accessor) =>
            {
                return Results.Ok(new
                {
                    tenantId = accessor.TenantId,
                    hasTenant = accessor.CurrentTenant.HasTenant,
                    status = accessor.SubscriptionStatus
                });
            });

            app.MapPost("/api/v1/test/mutation", () => Results.Ok(new { success = true }));
        }

        app.MapControllers();
    }

    private sealed class DefaultFallbackBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.NewGuid().ToString("N");
        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }
}
