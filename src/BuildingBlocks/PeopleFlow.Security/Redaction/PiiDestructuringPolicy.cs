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

        if (SensitiveDataRedactor.TryEnumerateDictionary(value, out var dictEntries))
        {
            var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictEntries.Count);
            foreach (var entry in dictEntries)
            {
                var logValue = SensitiveDataRedactor.IsSensitiveName(entry.Key)
                    ? new ScalarValue(SensitiveDataRedactor.Mask)
                    : propertyValueFactory.CreatePropertyValue(entry.Value, destructureObjects: true);
                elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue(entry.Key),
                    logValue));
            }

            result = new DictionaryValue(elements);
            return true;
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

                return new PropertyRule(p, IsSensitive: SensitiveDataRedactor.IsSensitiveName(p.Name), Mask: SensitiveDataRedactor.Mask);
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
                propValue = $"<error reading property: {SensitiveDataRedactor.SanitizeExceptionSummary(ex, 200)}>";
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
