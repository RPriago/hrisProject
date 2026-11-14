using Microsoft.AspNetCore.Identity;

namespace PeopleFlow.Security.Authentication;

/// <summary>
/// Provides cryptographic password and client-secret hashing using ASP.NET Core Identity's PBKDF2-HMAC-SHA512 hasher.
/// </summary>
public sealed class PasswordHasherService
{
    public const int MaxPasswordLength = 128;
    private static readonly object DummyUser = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string HashPassword(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        if (plaintext.Length > MaxPasswordLength)
        {
            throw new ArgumentException($"Password must not exceed {MaxPasswordLength} characters.", nameof(plaintext));
        }

        return _hasher.HashPassword(DummyUser, plaintext);
    }

    public bool VerifyPassword(string hashedValue, string providedPlaintext)
    {
        if (string.IsNullOrWhiteSpace(hashedValue) ||
            string.IsNullOrWhiteSpace(providedPlaintext) ||
            providedPlaintext.Length > MaxPasswordLength)
        {
            return false;
        }

        var result = _hasher.VerifyHashedPassword(DummyUser, hashedValue, providedPlaintext);
        return result == PasswordVerificationResult.Success ||
               result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}
