using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Persistence.Outbox;

public static class OutboxMessageStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Processed = "Processed";
    public const string DeadLetter = "DeadLetter";
}

public interface IOutboxPublisher
{
    Task<OutboxMessage> PublishAsync<T>(
        T message,
        Guid? correlationId = null,
        Guid? tenantId = null,
        bool autoSave = false,
        CancellationToken cancellationToken = default) where T : class;

    Task<OutboxMessage> PublishRawAsync(
        string messageType,
        string payloadJson,
        Guid? correlationId = null,
        Guid? tenantId = null,
        bool autoSave = false,
        CancellationToken cancellationToken = default);

    Task<OutboxMessage> PublishPlatformAsync(
        string messageType,
        string payloadJson,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default);
}
