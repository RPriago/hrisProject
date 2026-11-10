using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleFlow.Security.Csrf;

public sealed class CsrfTokenOptions
{
    public string? SecretKey { get; set; }
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(24);

    public int? TokenLifetimeMinutes
    {
        get => (int)TokenLifetime.TotalMinutes;
        set
        {
            if (value.HasValue)
            {
                TokenLifetime = TimeSpan.FromMinutes(value.Value);
            }
        }
    }
}

public sealed class CsrfTokenService : ICsrfTokenService
{
    private readonly byte[] _keyBytes;
    private readonly TimeSpan _tokenLifetime;
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

    public CsrfTokenService(CsrfTokenOptions? options = null)
    {
        _tokenLifetime = options?.TokenLifetime ?? TimeSpan.FromHours(24);
        if (!string.IsNullOrWhiteSpace(options?.SecretKey))
        {
            _keyBytes = Encoding.UTF8.GetBytes(options.SecretKey);
        }
        else
        {
            // Generate ephemeral 256-bit key if none is configured; production deployments bind a shared key from configuration.
            _keyBytes = RandomNumberGenerator.GetBytes(32);
        }
    }

    public string GenerateToken(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = FormattableString.Invariant($"{sessionId}:{timestamp}");
        var hash = HMACSHA256.HashData(_keyBytes, Encoding.UTF8.GetBytes(payload));
        var hexSignature = Convert.ToHexString(hash);

        return $"{timestamp}.{hexSignature}";
    }

    public bool ValidateToken(string? token, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var delimiterIndex = token.IndexOf('.');
        if (delimiterIndex <= 0 || delimiterIndex >= token.Length - 1)
        {
            return false;
        }

        var timestampSpan = token.AsSpan(0, delimiterIndex);
        var signatureSpan = token.AsSpan(delimiterIndex + 1);

        if (!long.TryParse(timestampSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var tokenEpochSeconds))
        {
            return false;
        }

        DateTimeOffset tokenTime;
        try
        {
            tokenTime = DateTimeOffset.FromUnixTimeSeconds(tokenEpochSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        if (tokenTime > now + ClockSkewTolerance || tokenTime < now - _tokenLifetime)
        {
            return false;
        }

        byte[] providedSignatureBytes;
        try
        {
            providedSignatureBytes = Convert.FromHexString(signatureSpan);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = FormattableString.Invariant($"{sessionId}:{tokenEpochSeconds}");
        var expectedHash = HMACSHA256.HashData(_keyBytes, Encoding.UTF8.GetBytes(payload));

        return CryptographicOperations.FixedTimeEquals(providedSignatureBytes, expectedHash);
    }
}
