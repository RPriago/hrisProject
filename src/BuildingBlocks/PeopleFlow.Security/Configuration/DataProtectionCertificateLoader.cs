using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace PeopleFlow.Security.Configuration;

/// <summary>
/// Loads X.509 certificates for ASP.NET Core Data Protection key-at-rest encryption.
/// </summary>
public static class DataProtectionCertificateLoader
{
    public static X509Certificate2? TryLoadFromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var certBase64 = configuration["Security:DataProtection:CertificateBase64"];
        var certPath = configuration["Security:DataProtection:CertificatePath"];
        var password = configuration["Security:DataProtection:CertificatePassword"];

        if (string.IsNullOrWhiteSpace(certBase64) && string.IsNullOrWhiteSpace(certPath))
        {
            return null;
        }

        byte[] rawBytes;
        if (!string.IsNullOrWhiteSpace(certBase64))
        {
            try
            {
                rawBytes = Convert.FromBase64String(certBase64.Trim());
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Security:DataProtection:CertificateBase64 is not a valid base64-encoded X.509 certificate.", ex);
            }
        }
        else
        {
            var resolvedPath = Path.GetFullPath(certPath!.Trim());
            if (!File.Exists(resolvedPath))
            {
                throw new InvalidOperationException(
                    $"Security:DataProtection:CertificatePath does not exist: '{resolvedPath}'.");
            }

            rawBytes = File.ReadAllBytes(resolvedPath);
        }

        try
        {
            // macOS Apple Security framework rejects EphemeralKeySet with PlatformNotSupportedException.
            var keyStorageFlags = OperatingSystem.IsMacOS()
                ? X509KeyStorageFlags.DefaultKeySet
                : X509KeyStorageFlags.EphemeralKeySet;

            var cert = X509CertificateLoader.LoadPkcs12(
                rawBytes,
                password,
                keyStorageFlags);

            if (!cert.HasPrivateKey)
            {
                cert.Dispose();
                throw new InvalidOperationException(
                    "The configured Data Protection certificate does not contain a private key.");
            }

            return cert;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Failed to load the configured Data Protection PKCS#12 certificate. Verify format and password.", ex);
        }
    }
}
