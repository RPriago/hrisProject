using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PeopleFlow.Jobs.Filters;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Jobs;
using PeopleFlow.Modules.Subscription.Core.Services;

namespace PeopleFlow.Modules.Subscription.Core;

public static class SubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BillingOptions>(configuration.GetSection("Billing"));
        services.TryAddScoped<IActiveApiClientCounter, DefaultActiveApiClientCounter>();
        services.AddScoped<ISubscriptionQuotaService, SubscriptionQuotaService>();
        services.AddScoped<ITenantSubscriptionQueryService, TenantSubscriptionQueryService>();
        services.AddScoped<ISubscriptionLifecycleService, SubscriptionLifecycleService>();
        services.AddScoped<BillingWebhookService>();
        services.AddScoped<IBillingWebhookService>(sp => sp.GetRequiredService<BillingWebhookService>());
        services.AddTransient<SubscriptionLifecycleSweepJob>();
        services.AddTransient<BillingWebhookRetryJob>();
        services.AddSingleton<ITenantSubscriptionValidator, JobSubscriptionValidator>();
        return services;
    }
}
