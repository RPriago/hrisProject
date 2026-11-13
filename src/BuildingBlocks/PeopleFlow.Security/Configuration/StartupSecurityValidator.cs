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
            connectionString.Contains("postgres_dev_secret", StringComparison.Ordinal) ||
            connectionString.Contains("peopleflow_migrator_secret", StringComparison.Ordinal) ||
            connectionString.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection must be explicitly configured without default or placeholder credentials in non-development environments.");
        }

        ValidateConnectionStringRole(connectionString);

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

        if (!configuration.GetValue<bool>("Storage:UseS3"))
        {
            throw new InvalidOperationException(
                "Storage:UseS3 must be enabled in non-development environments so private/quarantine file operations never fall back to no-op in-memory storage.");
        }

        if (requireWebSecurityOptions)
        {
            using var dpCert = DataProtectionCertificateLoader.TryLoadFromConfiguration(configuration);
            if (dpCert is null)
            {
                throw new InvalidOperationException(
                    "Security:DataProtection:CertificateBase64 or CertificatePath with private key must be configured in non-development environments to protect persisted Data Protection keys.");
            }
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

    private static void ValidateConnectionStringRole(string connectionString)
    {
        try
        {
            var csBuilder = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            string? username = null;
            if (csBuilder.TryGetValue("Username", out var u1))
            {
                username = u1?.ToString();
            }
            else if (csBuilder.TryGetValue("User ID", out var u2))
            {
                username = u2?.ToString();
            }

            if (string.Equals(username, "postgres", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(username, "peopleflow_migrator", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:DefaultConnection cannot use privileged role '{username}' outside Development or Testing.");
            }
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection could not be parsed safely.", ex);
        }
    }
}
