using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Jobs;
using PeopleFlow.Modules.Tenancy.Core.Services;

namespace PeopleFlow.Modules.Tenancy.Core;

public static class TenancyServiceCollectionExtensions
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddScoped<TenantRoleSeeder>();
        services.AddScoped<PermissionAndScopeResolver>();
        services.AddScoped<IPermissionAndScopeResolver>(sp => sp.GetRequiredService<PermissionAndScopeResolver>());

        services.AddScoped<RoleManagementService>();
        services.AddScoped<IRoleManagementService>(sp => sp.GetRequiredService<RoleManagementService>());

        services.AddScoped<SodPolicyEvaluator>();
        services.AddScoped<ISodPolicyEvaluator>(sp => sp.GetRequiredService<SodPolicyEvaluator>());

        services.AddScoped<TenantMembershipReader>();
        services.AddScoped<ITenantMembershipReader>(sp => sp.GetRequiredService<TenantMembershipReader>());

        services.AddScoped<InvitationService>();
        services.AddScoped<IInvitationService>(sp => sp.GetRequiredService<InvitationService>());

        services.AddScoped<MemberManagementService>();
        services.AddScoped<IMemberManagementService>(sp => sp.GetRequiredService<MemberManagementService>());

        services.AddScoped<TenantSwitcherService>();
        services.AddScoped<ITenantSwitcherService>(sp => sp.GetRequiredService<TenantSwitcherService>());

        services.AddScoped<TenantOffboardingAccessRevoker>();
        services.AddScoped<ITenantOffboardingAccessRevoker>(sp => sp.GetRequiredService<TenantOffboardingAccessRevoker>());

        services.AddSingleton<ApiClientRateLimiter>();
        services.AddScoped<ApiClientService>();
        services.AddScoped<IApiClientService>(sp => sp.GetRequiredService<ApiClientService>());

        services.AddScoped<ApiClientAuthenticator>();
        services.AddScoped<IApiClientAuthenticator>(sp => sp.GetRequiredService<ApiClientAuthenticator>());
        services.AddScoped<IActiveApiClientCounter, ActiveApiClientCounter>();

        services.AddScoped<PlatformTenantService>();
        services.AddScoped<IPlatformTenantService>(sp => sp.GetRequiredService<PlatformTenantService>());

        services.AddScoped<SupportAccessGrantService>();
        services.AddScoped<ISupportAccessGrantService>(sp => sp.GetRequiredService<SupportAccessGrantService>());

        services.AddTransient<ExpiredAccessCleanupJob>();

        services.TryAddScoped<IOrganizationUnitValidator, DefaultOrganizationUnitValidator>();
        services.TryAddScoped<IActiveHeadcountCounter, DefaultActiveHeadcountCounter>();

        return services;
    }
}
