using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.States;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
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
using PeopleFlow.Persistence.DataProtection;
using PeopleFlow.Persistence.Idempotency;
using PeopleFlow.Persistence.Outbox;
using PeopleFlow.Security.Authorization;
using PeopleFlow.Security.Configuration;
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

        StartupSecurityValidator.ValidateHostConfiguration(
            builder.Configuration,
            builder.Environment,
            requireWebSecurityOptions: true);

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(Program).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        var dpBuilder = builder.Services.AddDataProtection()
            .SetApplicationName("PeopleFlow")
            .PersistKeysToPeopleFlowDbContext();

        var dpCert = DataProtectionCertificateLoader.TryLoadFromConfiguration(builder.Configuration);
        if (dpCert != null)
        {
            dpBuilder.ProtectKeysWithCertificate(dpCert);
        }

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookieOptions =>
            {
                cookieOptions.Cookie.Name = PeopleFlow.Security.Authentication.SessionCookieConfig.CookieName;
                cookieOptions.Cookie.HttpOnly = true;
                cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
                cookieOptions.Cookie.Path = "/";
                cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(8);
                cookieOptions.SlidingExpiration = false;
                cookieOptions.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx => WriteAuthProblemAsync(
                        ctx.HttpContext,
                        StatusCodes.Status401Unauthorized,
                        "AUTH_REQUIRED",
                        "Authentication is required to access this resource."),
                    OnRedirectToAccessDenied = ctx => WriteAuthProblemAsync(
                        ctx.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "FORBIDDEN",
                        "You do not have permission to access this resource.")
                };
            });
        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();
        builder.Services.AddSingleton<ICurrentActorContext, AsyncLocalCurrentActorContext>();
        builder.Services.AddSingleton<IAuthorizationGuard, FourTierAuthorizationPipeline>();
        builder.Services.TryAddSingleton<IDestructuringPolicy, PiiDestructuringPolicy>();

        var csrfOptions = new CsrfTokenOptions();
        builder.Configuration.GetSection("Security:Csrf").Bind(csrfOptions);
        csrfOptions.RequireConfiguredKey = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
        builder.Services.TryAddSingleton(csrfOptions);
        builder.Services.TryAddSingleton<ICsrfTokenService>(sp => new CsrfTokenService(sp.GetRequiredService<CsrfTokenOptions>()));

        var allowedOrigins = builder.Configuration.GetSection("Security:Csrf:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        var csrfValidationOptions = new CsrfValidationOptions
        {
            AllowedOrigins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase),
            AllowSelfOriginFallback = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"),
            RequireTokenWhenSessionCookieAbsent = true,
            ExemptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "/api/v1/auth/csrf",
                "/healthz",
                "/api/v1/test/unhandled-error"
            }
        };
        builder.Services.AddSingleton(csrfValidationOptions);

        builder.Services.Configure<ForwardedHeadersOptions>(forwardedOptions =>
        {
            forwardedOptions.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwardedOptions.ForwardLimit = 1;
            forwardedOptions.KnownNetworks.Clear();
            forwardedOptions.KnownProxies.Clear();

            var knownProxies = builder.Configuration.GetSection("Security:ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
            foreach (var rawProxy in knownProxies)
            {
                if (string.IsNullOrWhiteSpace(rawProxy)) continue;

                if (rawProxy.Contains('/') && System.Net.IPNetwork.TryParse(rawProxy, out var network))
                {
                    forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(network.BaseAddress, network.PrefixLength));
                }
                else if (IPAddress.TryParse(rawProxy, out var proxyIp))
                {
                    forwardedOptions.KnownProxies.Add(proxyIp);
                }
            }
        });

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
        builder.Services.AddSingleton<IFileQuarantineScanner>(sp =>
        {
            var opts = sp.GetRequiredService<StorageOptions>();
            return opts.UseClamAvDaemon
                ? new ClamAvDaemonScanner(opts)
                : new MockOrClamAvScanner();
        });
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
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
                {
                    connectionString = "Host=localhost;Database=peopleflow_dev;Username=peopleflow_app;Password=peopleflow_app_secret";
                }
                else
                {
                    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required in non-development environments.");
                }
            }

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
            if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
            {
                try
                {
                    return new BackgroundJobClient(JobStorage.Current);
                }
                catch
                {
                    return new DefaultFallbackBackgroundJobClient();
                }
            }

            return new BackgroundJobClient(JobStorage.Current);
        });
        builder.Services.AddScoped<IBackgroundJobService, HangfireBackgroundJobService>();
        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("auth-csrf", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("healthz", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        builder.Services.AddRuntimeLogRedaction();

        return builder;
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        // Enforce pipeline order: ForwardedHeaders -> CorrelationId -> SecurityHeaders -> ProblemDetails -> Routing -> Csrf -> TenantResolution -> Controllers
        app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
        }
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<ProblemDetailsMiddleware>();

        app.UseRouting();
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMiddleware<CsrfValidationMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();

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

    private static async Task WriteAuthProblemAsync(HttpContext context, int statusCode, string code, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var payload = new
        {
            type = $"https://httpstatuses.com/{statusCode}",
            title = statusCode == StatusCodes.Status401Unauthorized ? "Unauthorized" : "Forbidden",
            status = statusCode,
            code,
            detail,
            instance = context.Request.Path.Value,
            correlationId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private sealed class DefaultFallbackBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.NewGuid().ToString("N");
        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }
}
