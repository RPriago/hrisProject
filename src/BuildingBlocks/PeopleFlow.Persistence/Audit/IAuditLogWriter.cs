using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Persistence.Audit;

/// <summary>
/// Contract for writing sensitive change audit trails atomically across modules into outbox messages.
/// </summary>
public interface IAuditLogWriter
{
    Task<OutboxMessage> WriteAsync(
        AuditLogEntry entry,
        bool autoSave = false,
        CancellationToken cancellationToken = default);

    Task<OutboxMessage> RecordAsync(
        AuditLogEntry entry,
        CancellationToken cancellationToken = default);
}
