using System.Reflection;
using System.Text.Json;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;
using PeopleFlow.Persistence.Outbox;
using PeopleFlow.Security.Redaction;

namespace PeopleFlow.Persistence.Audit;

/// <summary>
/// Writes audit entries as outbox messages atomically within the active PeopleFlowDbContext transaction.
/// Preserves caller correlationId, redacts sensitive keys/PII, and stages without autosave by default.
/// </summary>
public sealed class OutboxAuditLogWriter : IAuditLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] SensitiveKeySubstrings =
    [
        "Password", "Token", "Secret", "Nik", "BankAccount", "CreditCard",
        "ApiKey", "PrivateKey", "NationalId", "Npwp", "Salary", "Wage",
        "Otp", "Rekening", "Payslip", "SlipGaji"
    ];

    private readonly PeopleFlowDbContext _dbContext;

    public OutboxAuditLogWriter(PeopleFlowDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<OutboxMessage> WriteAsync(
        AuditLogEntry entry,
        bool autoSave = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var sanitizedOldValues = SanitizeValues(entry.OldValues);
        var sanitizedNewValues = SanitizeValues(entry.NewValues);
        var sanitizedEntry = entry with
        {
            OldValues = sanitizedOldValues,
            NewValues = sanitizedNewValues
        };

        var outboxMessage = new OutboxMessage(Guid.CreateVersion7())
        {
            TenantId = entry.TenantId,
            CorrelationId = entry.CorrelationId,
            MessageType = "AuditLogRecorded",
            PayloadJson = JsonSerializer.Serialize(sanitizedEntry, JsonOptions),
            Status = OutboxMessageStatus.Pending,
            NextAttemptAtUtc = entry.OccurredAtUtc ?? DateTimeOffset.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        if (autoSave)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return outboxMessage;
    }

    public Task<OutboxMessage> RecordAsync(
        AuditLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(entry, autoSave: false, cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?>? SanitizeValues(
        IReadOnlyDictionary<string, object?>? values,
        int depth = 0,
        HashSet<object>? visited = null)
    {
        if (values == null)
        {
            return null;
        }

        var sanitized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values)
        {
            if (IsSensitiveKey(key))
            {
                sanitized[key] = "****";
            }
            else
            {
                sanitized[key] = SanitizeValue(value, depth, visited);
            }
        }

        return sanitized;
    }

    private static object? SanitizeValue(object? value, int depth = 0, HashSet<object>? visited = null)
    {
        if (value == null)
        {
            return null;
        }

        if (depth > 10)
        {
            return "[MAX_DEPTH]";
        }

        var type = value.GetType();

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan))
        {
            return value;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDict)
        {
            return SanitizeValues(readOnlyDict, depth + 1, visited);
        }

        if (value is IDictionary<string, object?> dict)
        {
            return SanitizeValues(new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase), depth + 1, visited);
        }

        var typeSensitiveAttr = type.GetCustomAttribute<SensitiveDataAttribute>(inherit: true);
        if (typeSensitiveAttr != null)
        {
            return typeSensitiveAttr.Mask;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            var sanitizedList = new List<object?>();
            foreach (var item in enumerable)
            {
                sanitizedList.Add(SanitizeValue(item, depth + 1, visited));
            }
            return sanitizedList;
        }

        visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!visited.Add(value))
        {
            return "[CIRCULAR_REFERENCE]";
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        var objDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in properties)
        {
            var sensitiveAttr = prop.GetCustomAttribute<SensitiveDataAttribute>(inherit: true);
            if (sensitiveAttr != null)
            {
                objDict[prop.Name] = sensitiveAttr.Mask;
            }
            else if (IsSensitiveKey(prop.Name))
            {
                objDict[prop.Name] = "****";
            }
            else
            {
                var propVal = prop.GetValue(value);
                objDict[prop.Name] = SanitizeValue(propVal, depth + 1, visited);
            }
        }

        return objDict;
    }

    private static bool IsSensitiveKey(string key)
    {
        return SensitiveKeySubstrings.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}
