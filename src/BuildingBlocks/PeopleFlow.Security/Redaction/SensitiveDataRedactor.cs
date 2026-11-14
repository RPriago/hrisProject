using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        redacted = EmailAddressRegex().Replace(redacted, "****@****");
        return redacted;
    }

    /// <summary>
    /// Recursively clones and redacts arbitrary objects, dictionaries, enumerables, strings,
    /// and exceptions into JSON-safe structures, breaking cycles cleanly.
    /// </summary>
    public static object? SanitizeObjectGraph(object? value, int depth = 0, HashSet<object>? visited = null)
    {
        if (value is null)
        {
            return null;
        }

        if (depth >= 8)
        {
            return "[Truncated]";
        }

        if (value is string str)
        {
            return RedactSecretsFromText(str);
        }

        if (value is Exception ex)
        {
            return SanitizeExceptionSummary(ex);
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is decimal or Guid or DateTime or DateTimeOffset or TimeSpan)
        {
            return value;
        }

        visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!visited.Add(value))
        {
            return "[Circular]";
        }

        try
        {
            if (TryEnumerateDictionary(value, out var dictEntries))
            {
                var safeDict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var entry in dictEntries)
                {
                    safeDict[entry.Key] = IsSensitiveName(entry.Key)
                        ? DefaultMask
                        : SanitizeObjectGraph(entry.Value, depth + 1, visited);
                }

                return safeDict;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                var list = new List<object?>();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ >= 100)
                    {
                        list.Add("[Truncated]");
                        break;
                    }

                    list.Add(SanitizeObjectGraph(item, depth + 1, visited));
                }

                return list;
            }

            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var safeObj = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in props)
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                var sensitiveAttr = prop.GetCustomAttributes(typeof(SensitiveDataAttribute), inherit: true)
                    .OfType<SensitiveDataAttribute>()
                    .FirstOrDefault();

                if (sensitiveAttr != null)
                {
                    safeObj[prop.Name] = sensitiveAttr.Mask;
                    continue;
                }

                if (IsSensitiveName(prop.Name))
                {
                    safeObj[prop.Name] = DefaultMask;
                    continue;
                }

                object? propValue;
                try
                {
                    propValue = prop.GetValue(value);
                }
                catch
                {
                    safeObj[prop.Name] = "[Unreadable]";
                    continue;
                }

                safeObj[prop.Name] = SanitizeObjectGraph(propValue, depth + 1, visited);
            }

            return safeObj;
        }
        finally
        {
            visited.Remove(value);
        }
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

    /// <summary>
    /// Redacts sensitive keys, inline secrets, and emails from JSON payloads while optionally
    /// keeping pre-allowlisted properties (such as DataProtection-encrypted outbox tokens) intact.
    /// </summary>
    public static string RedactJson(string? json, IReadOnlySet<string>? preserveProperties = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return "{}";
            }

            return SanitizeJsonNode(node, preserveProperties, 0)?.ToJsonString() ?? "{}";
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { rawRedacted = RedactSecretsFromText(json) });
        }
    }

    private static JsonNode? SanitizeJsonNode(JsonNode? node, IReadOnlySet<string>? preserveProperties, int depth)
    {
        if (node is null)
        {
            return null;
        }

        if (depth >= 12)
        {
            return JsonValue.Create("[Truncated]");
        }

        if (node is JsonObject obj)
        {
            var sanitized = new JsonObject();
            foreach (var kv in obj)
            {
                if (preserveProperties != null &&
                    preserveProperties.Contains(kv.Key) &&
                    kv.Value is JsonValue preservedVal &&
                    preservedVal.TryGetValue<string>(out var rawPreserved))
                {
                    sanitized[kv.Key] = JsonValue.Create(rawPreserved);
                    continue;
                }

                sanitized[kv.Key] = IsSensitiveName(kv.Key)
                    ? JsonValue.Create(DefaultMask)
                    : SanitizeJsonNode(kv.Value, preserveProperties, depth + 1);
            }

            return sanitized;
        }

        if (node is JsonArray arr)
        {
            var sanitized = new JsonArray();
            var count = 0;
            foreach (var item in arr)
            {
                if (count++ >= 100)
                {
                    sanitized.Add(JsonValue.Create("[Truncated]"));
                    break;
                }

                sanitized.Add(SanitizeJsonNode(item, preserveProperties, depth + 1));
            }

            return sanitized;
        }

        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            return JsonValue.Create(RedactSecretsFromText(text));
        }

        return node.DeepClone();
    }

    [GeneratedRegex(@"(?i)\b(Password|Pwd|Secret|ApiKey|AccessKey|PrivateKey|Token|Passphrase|Email|Nik|Ktp|Npwp|Salary|BankAccount)\s*=\s*([^;\s,'""]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringSecretRegex();

    [GeneratedRegex(@"(?i)\b(Bearer|Basic)\s+([A-Za-z0-9._~+/=-]{4,})", RegexOptions.CultureInvariant)]
    private static partial Regex AuthSchemeTokenRegex();

    [GeneratedRegex(@"(https?://|postgres(?:ql)?://|redis://)([^:@/\s]+):([^@/\s]+)@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriCredentialsRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressRegex();
}
