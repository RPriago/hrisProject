using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace PeopleFlow.Security.Redaction;

/// <summary>
/// Serilog destructuring policy that automatically redacts sensitive data (PII, credentials, financial details)
/// from structured log events using either [SensitiveData] annotations or well-known sensitive property names.
/// </summary>
public sealed class PiiDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, PropertyRule[]> TypeCache = new();

    private static readonly string[] SensitiveNamePatterns =
    [
        "Password",
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
        "SlipGaji"
    ];

    private sealed record PropertyRule(
        PropertyInfo Property,
        bool IsSensitive,
        string Mask);

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        result = null;
        if (value is null)
        {
            return false;
        }

        var type = value.GetType();

        // Let Serilog handle primitives, scalars, collections, and standard system types
        if (type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan) ||
            type == typeof(Guid) ||
            type == typeof(byte[]) ||
            (type.Namespace != null && (type.Namespace.StartsWith("System") || type.Namespace.StartsWith("Microsoft"))))
        {
            return false;
        }

        var rules = TypeCache.GetOrAdd(type, static t =>
        {
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

            return properties.Select(p =>
            {
                var attr = p.GetCustomAttribute<SensitiveDataAttribute>(inherit: true);
                if (attr != null)
                {
                    return new PropertyRule(p, IsSensitive: true, Mask: attr.Mask);
                }

                var matchesPattern = SensitiveNamePatterns.Any(pattern =>
                    p.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));

                return new PropertyRule(p, IsSensitive: matchesPattern, Mask: "****");
            }).ToArray();
        });

        var logProperties = new List<LogEventProperty>(rules.Length);
        foreach (var rule in rules)
        {
            object? propValue;
            try
            {
                propValue = rule.Property.GetValue(value);
            }
            catch (Exception ex)
            {
                propValue = $"<error reading property: {ex.Message}>";
            }

            LogEventPropertyValue propertyVal;
            if (rule.IsSensitive)
            {
                propertyVal = new ScalarValue(rule.Mask);
            }
            else
            {
                propertyVal = propertyValueFactory.CreatePropertyValue(propValue, destructureObjects: true);
            }

            logProperties.Add(new LogEventProperty(rule.Property.Name, propertyVal));
        }

        result = new StructureValue(logProperties, type.Name);
        return true;
    }
}
