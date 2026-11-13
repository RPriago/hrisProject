using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PeopleFlow.Security.Configuration;

/// <summary>
/// Enforces fail-fast security posture validation during host startup outside Development/Testing.
/// </summary>
public static class StartupSecurityValidator
{
    public static void ValidateHostConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool requireWebSecurityOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString) ||
            connectionString.Contains("peopleflow_app_secret", StringComparison.Ordinal) ||
            connectionString.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection must be explicitly configured without default or placeholder credentials in non-development environments.");
        }

        if (configuration.GetValue<bool>("Security:AllowHeaderContextOverride"))
        {
            throw new InvalidOperationException(
                "Security:AllowHeaderContextOverride cannot be enabled outside Development or Testing.");
        }

        if (!configuration.GetValue<bool>("Storage:UseClamAvDaemon"))
        {
            throw new InvalidOperationException(
                "Storage:UseClamAvDaemon must be enabled in non-development environments so malware scanning never falls back to a mock scanner.");
        }

        if (!requireWebSecurityOptions)
        {
            return;
        }

        var csrfSecret = configuration["Security:Csrf:SecretKey"];
        if (string.IsNullOrWhiteSpace(csrfSecret) ||
            Encoding.UTF8.GetByteCount(csrfSecret) < 32 ||
            csrfSecret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            csrfSecret.Contains("sample-32-byte", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Security:Csrf:SecretKey must be explicitly configured with at least 32 bytes in non-development environments.");
        }

        var allowedHosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) ||
            allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(h => h == "*"))
        {
            throw new InvalidOperationException(
                "AllowedHosts cannot be empty or wildcard '*' outside Development/Testing.");
        }

        var allowedOrigins = configuration.GetSection("Security:Csrf:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        foreach (var origin in allowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin) ||
                origin.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                origin.Contains("[::1]", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Security:Csrf:AllowedOrigins contains insecure or local origin '{origin}' in non-development environment.");
            }
        }
    }
}
