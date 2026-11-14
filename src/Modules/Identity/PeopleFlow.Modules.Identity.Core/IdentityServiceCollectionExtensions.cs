using Microsoft.Extensions.DependencyInjection;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Identity.Core.Services;
using PeopleFlow.Security.Authentication;

namespace PeopleFlow.Modules.Identity.Core;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddSingleton<PasswordHasherService>();
        services.AddSingleton<TotpService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IIdentityAuthService>(sp => sp.GetRequiredService<AuthService>());
        services.AddScoped<IIdentityProvisioningService>(sp => sp.GetRequiredService<AuthService>());
        services.AddScoped<SessionService>();
        services.AddScoped<IUserSessionRevoker>(sp => sp.GetRequiredService<SessionService>());
        services.AddScoped<ISessionContextResolver>(sp => sp.GetRequiredService<SessionService>());
        return services;
    }
}
