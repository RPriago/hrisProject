using System.Text.Json;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Persistence.Outbox;

public class OutboxPublisher : IOutboxPublisher
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly ITenantContextAccessor? _tenantContextAccessor;
    private readonly IClock? _clock;

    public OutboxPublisher(
        PeopleFlowDbContext dbContext,
        ITenantContextAccessor? tenantContextAccessor = null,
        IClock? clock = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContextAccessor = tenantContextAccessor;
        _clock = clock;
    }

    public Task<OutboxMessage> PublishAsync<T>(
        T message,
        Guid? correlationId = null,
        Guid? tenantId = null,
        bool autoSave = false,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = typeof(T).FullName ?? typeof(T).Name;
        var payloadJson = JsonSerializer.Serialize(message);

        return PublishRawAsync(messageType, payloadJson, correlationId, tenantId, autoSave, cancellationToken);
    }

    public async Task<OutboxMessage> PublishRawAsync(
        string messageType,
        string payloadJson,
        Guid? correlationId = null,
        Guid? tenantId = null,
        bool autoSave = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("Message type cannot be empty.", nameof(messageType));
        }

        var effectiveTenantId = tenantId ?? _tenantContextAccessor?.TenantId ?? _dbContext.CurrentTenantId;
        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        var outboxMessage = new OutboxMessage
        {
            TenantId = effectiveTenantId,
            MessageType = messageType,
            PayloadJson = payloadJson ?? string.Empty,
            CorrelationId = correlationId ?? Guid.CreateVersion7(),
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            NextAttemptAtUtc = now
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        if (autoSave)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return outboxMessage;
    }
}
