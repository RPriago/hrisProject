using System.Collections;
using System.Text.RegularExpressions;

namespace PeopleFlow.Security.Redaction;

/// <summary>
/// Shared fail-closed redaction helpers for PII keys, dictionaries, exception messages, and inline secrets.
/// </summary>
public static partial class SensitiveDataRedactor
{
    private const string DefaultMask = "****";

    private static readonly string[] SensitiveNamePatterns =
    [
        "Password",
        "Passphrase",
        "Token",
        "Secret",
        "Nik",
        "BankAccount",
        "CreditCard",
        "ApiKey",
        "PrivateKey",
        "NationalId",
        "Npwp",
        "Salary",
        "Wage",
        "Otp",
        "Rekening",
        "Payslip",
        "SlipGaji",
        "Authorization",
        "Bearer",
        "Cookie",
        "ConnectionString"
    ];

    public static bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var pattern in SensitiveNamePatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryEnumerateDictionary(
        object? value,
        out List<KeyValuePair<string, object?>> entries)
    {
        entries = [];
        if (value is null)
        {
            return false;
        }

        if (value is IReadOnlyDictionary<string, object?> roObj)
        {
            foreach (var kv in roObj)
            {
                entries.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
            }
            return true;
        }

        if (value is IReadOnlyDictionary<string, string> roStr)
        {
            foreach (var kv in roStr)
            {
                entries.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
            }
            return true;
        }

        if (value is IDictionary nonGenericDict)
        {
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                var key = entry.Key?.ToString() ?? string.Empty;
                entries.Add(new KeyValuePair<string, object?>(key, entry.Value));
            }
            return true;
        }

        var type = value.GetType();
        var isGenericDict = type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
             i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

        if (isGenericDict && value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                var itemType = item.GetType();
                var keyProp = itemType.GetProperty("Key");
                var valProp = itemType.GetProperty("Value");
                if (keyProp != null && valProp != null)
                {
                    var key = keyProp.GetValue(item)?.ToString() ?? string.Empty;
                    entries.Add(new KeyValuePair<string, object?>(key, valProp.GetValue(item)));
                }
            }
            return true;
        }

        return false;
    }

    public static string RedactSecretsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var redacted = ConnectionStringSecretRegex().Replace(text, "$1=****");
        redacted = AuthSchemeTokenRegex().Replace(redacted, "$1 ****");
        redacted = UriCredentialsRegex().Replace(redacted, "$1****:****@");
        return redacted;
    }

    public static string SanitizeExceptionSummary(Exception exception, int maxLength = 500)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var sanitizedMessage = RedactSecretsFromText(exception.Message);
        var summary = string.IsNullOrWhiteSpace(sanitizedMessage)
            ? exception.GetType().Name
            : $"{exception.GetType().Name}: {sanitizedMessage}";

        return summary.Length > maxLength ? summary[..maxLength] : summary;
    }

    public static string Mask => DefaultMask;

    [GeneratedRegex(@"(?i)\b(Password|Pwd|Secret|ApiKey|AccessKey|PrivateKey|Token|Passphrase)\s*=\s*([^;\s,'""]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringSecretRegex();

    [GeneratedRegex(@"(?i)\b(Bearer|Basic)\s+([A-Za-z0-9._~+/=-]{4,})", RegexOptions.CultureInvariant)]
    private static partial Regex AuthSchemeTokenRegex();

    [GeneratedRegex(@"(https?://|postgres(?:ql)?://|redis://)([^:@/\s]+):([^@/\s]+)@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriCredentialsRegex();
}
