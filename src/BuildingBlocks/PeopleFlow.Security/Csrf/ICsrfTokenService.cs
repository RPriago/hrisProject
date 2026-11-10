namespace PeopleFlow.Security.Csrf;

public interface ICsrfTokenService
{
    /// <summary>
    /// Generates a cryptographically signed anti-CSRF token bound to the specific browser session and current timestamp.
    /// </summary>
    string GenerateToken(string sessionId);

    /// <summary>
    /// Verifies the anti-CSRF token signature, session affinity, and expiration in constant time.
    /// </summary>
    bool ValidateToken(string? token, string? sessionId);
}
