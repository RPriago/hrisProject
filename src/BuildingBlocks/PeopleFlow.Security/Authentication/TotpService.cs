using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace PeopleFlow.Security.Authentication;

/// <summary>
/// RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second window) with DataProtection encryption at rest.
/// </summary>
public sealed class TotpService
{
    private const string ProtectorPurpose = "PeopleFlow.Identity.TotpSecret.v1";
    private const string OutboxSecretProtectorPurpose = "PeopleFlow.Outbox.PasswordResetToken.v1";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int StepSeconds = 30;
    private const int CodeDigits = 6;

    private readonly IDataProtector _protector;
    private readonly IDataProtector _outboxSecretProtector;

    public TotpService(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _outboxSecretProtector = dataProtectionProvider.CreateProtector(OutboxSecretProtectorPurpose);
    }

    public string GenerateBase32Secret(int byteLength = 20)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return ToBase32(bytes);
    }

    public string ProtectSecret(string base32Secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base32Secret);
        return _protector.Protect(base32Secret);
    }

    public string UnprotectSecret(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        return _protector.Unprotect(protectedSecret);
    }

    public string ProtectOutboxSecret(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return _outboxSecretProtector.Protect(plaintext);
    }

    public string UnprotectOutboxSecret(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        return _outboxSecretProtector.Unprotect(protectedValue);
    }

    public string BuildOtpAuthUri(string issuer, string accountEmail, string base32Secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountEmail}");
        var encIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={encIssuer}&algorithm=SHA1&digits={CodeDigits}&period={StepSeconds}";
    }

    public string ComputeCode(string base32Secret, DateTimeOffset timestampUtc)
    {
        var key = FromBase32(base32Secret);
        var counter = timestampUtc.ToUnixTimeSeconds() / StepSeconds;
        return ComputeHotp(key, counter);
    }

    public bool VerifyCode(string base32Secret, string code, DateTimeOffset timestampUtc, out long matchedStep, int allowedDriftSteps = 1)
    {
        matchedStep = 0;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalized = code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != CodeDigits || !normalized.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = FromBase32(base32Secret);
        var currentStep = timestampUtc.ToUnixTimeSeconds() / StepSeconds;
        var providedBytes = Encoding.ASCII.GetBytes(normalized);

        for (var offset = -allowedDriftSteps; offset <= allowedDriftSteps; offset++)
        {
            var step = currentStep + offset;
            if (step < 0) continue;

            var expected = ComputeHotp(key, step);
            var expectedBytes = Encoding.ASCII.GetBytes(expected);
            if (CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    private static string ComputeHotp(byte[] key, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[20];
        using var hmac = new HMACSHA1(key);
        hmac.TryComputeHash(counterBytes, hash, out _);

        var offset = hash[19] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        var otp = binary % 1_000_000;
        return otp.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ToBase32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
        {
            sb.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }
        return sb.ToString();
    }

    private static byte[] FromBase32(string base32)
    {
        var clean = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;

        foreach (var c in clean)
        {
            var val = Base32Alphabet.IndexOf(c);
            if (val < 0)
            {
                throw new FormatException("Invalid Base32 character.");
            }

            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return bytes.ToArray();
    }
}
