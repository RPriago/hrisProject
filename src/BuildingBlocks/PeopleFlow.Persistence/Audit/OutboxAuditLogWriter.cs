using System.Text.Json;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;
using PeopleFlow.Persistence.Outbox;
using PeopleFlow.Security.Redaction;

namespace PeopleFlow.Persistence.Audit;

/// <summary>
/// Writes immutable, PII-redacted <see cref="AuditLogEntry"/> snapshots into the transactional Outbox.
/// Honors <see cref="SensitiveDataAttribute"/> and key-based PII conventions to prevent secret persistence in audit trails.
/// </summary>
public sealed class OutboxAuditLogWriter : IAuditLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var sanitizedReason = entry.Reason == null
            ? null
            : SensitiveDataRedactor.RedactSecretsFromText(entry.Reason);

        var sanitizedEntry = entry with
        {
            Reason = sanitizedReason,
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
        IReadOnlyDictionary<string, object?>? values)
    {
        if (values == null)
        {
            return null;
        }

        return SensitiveDataRedactor.SanitizeObjectGraph(values) as IReadOnlyDictionary<string, object?>;
    }
}
