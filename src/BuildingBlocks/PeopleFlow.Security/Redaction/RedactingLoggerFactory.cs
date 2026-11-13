using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PeopleFlow.Security.Redaction;

/// <summary>
/// Wraps <see cref="LoggerFactory"/> at runtime so every registered <see cref="ILoggerProvider"/>
/// receives pre-redacted structured properties, PII-sanitized payloads, and sanitized exceptions.
/// </summary>
public sealed partial class RedactingLoggerFactory : ILoggerFactory
{
    private readonly LoggerFactory _innerFactory;

    public RedactingLoggerFactory(
        IEnumerable<ILoggerProvider> providers,
        IOptionsMonitor<LoggerFilterOptions> filterOption,
        IOptions<LoggerFactoryOptions>? factoryOptions = null)
    {
        _innerFactory = new LoggerFactory(providers, filterOption, factoryOptions);
    }

    public void AddProvider(ILoggerProvider provider) => _innerFactory.AddProvider(provider);

    public ILogger CreateLogger(string categoryName) => new RedactingLogger(_innerFactory.CreateLogger(categoryName));

    public void Dispose() => _innerFactory.Dispose();

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is string str)
            {
                return inner.BeginScope(SensitiveDataRedactor.RedactSecretsFromText(str));
            }

            return inner.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var safeException = exception != null
                ? new SanitizedLogException(SensitiveDataRedactor.SanitizeExceptionSummary(exception))
                : null;

            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                var safePairs = new List<KeyValuePair<string, object?>>();
                string? originalFormat = null;

                foreach (var kv in pairs)
                {
                    if (string.Equals(kv.Key, "{OriginalFormat}", StringComparison.Ordinal))
                    {
                        originalFormat = kv.Value?.ToString();
                        safePairs.Add(kv);
                        continue;
                    }

                    object? safeValue;
                    if (SensitiveDataRedactor.IsSensitiveName(kv.Key))
                    {
                        safeValue = SensitiveDataRedactor.Mask;
                    }
                    else
                    {
                        safeValue = FormatSafeValue(SensitiveDataRedactor.SanitizeObjectGraph(kv.Value));
                    }

                    safePairs.Add(new KeyValuePair<string, object?>(kv.Key, safeValue));
                }

                var rendered = originalFormat != null
                    ? RenderTemplate(originalFormat, safePairs)
                    : SensitiveDataRedactor.RedactSecretsFromText(formatter(state, safeException));

                var safeState = new RedactedLogState(safePairs, rendered);
                inner.Log(logLevel, eventId, safeState, safeException, static (s, _) => s.RenderedMessage);
                return;
            }

            var safeMessage = SensitiveDataRedactor.RedactSecretsFromText(formatter(state, safeException));
            inner.Log(logLevel, eventId, safeMessage, safeException, static (msg, _) => msg);
        }
    }

    private static object? FormatSafeValue(object? sanitized)
    {
        if (sanitized is null or string or ValueType)
        {
            return sanitized;
        }

        return new StructuredLogValue(sanitized);
    }

    private static string RenderTemplate(string template, IReadOnlyList<KeyValuePair<string, object?>> pairs)
    {
        var lookup = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in pairs)
        {
            if (!string.Equals(kv.Key, "{OriginalFormat}", StringComparison.Ordinal))
            {
                lookup[kv.Key] = kv.Value;
            }
        }

        var result = PlaceholderRegex().Replace(template, match =>
        {
            var rawName = match.Groups["name"].Value;
            var keyName = rawName.TrimStart('@', '$');
            if (lookup.TryGetValue(rawName, out var val) || lookup.TryGetValue(keyName, out val))
            {
                return val?.ToString() ?? "(null)";
            }

            return match.Value;
        });

        return SensitiveDataRedactor.RedactSecretsFromText(result);
    }

    [GeneratedRegex(@"\{(?<name>[@$]?\w+)(?:[^}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    private sealed class StructuredLogValue(object value)
    {
        public override string ToString()
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch
            {
                return SensitiveDataRedactor.Mask;
            }
        }
    }

    private sealed class RedactedLogState(IReadOnlyList<KeyValuePair<string, object?>> values, string renderedMessage)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public string RenderedMessage { get; } = renderedMessage;
        public KeyValuePair<string, object?> this[int index] => values[index];
        public int Count => values.Count;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => values.GetEnumerator();
        public override string ToString() => RenderedMessage;
    }

    private sealed class SanitizedLogException(string summary) : Exception(summary)
    {
        public override string? StackTrace => null;
        public override string ToString() => Message;
    }
}

public static class RuntimeLogRedactionServiceCollectionExtensions
{
    /// <summary>
    /// Installs <see cref="RedactingLoggerFactory"/> as the host <see cref="ILoggerFactory"/>
    /// while preserving all registered <see cref="ILoggerProvider"/> instances.
    /// </summary>
    public static IServiceCollection AddRuntimeLogRedaction(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(ILoggerFactory))
            {
                services.RemoveAt(i);
            }
        }

        services.AddSingleton<ILoggerFactory, RedactingLoggerFactory>();
        return services;
    }
}
