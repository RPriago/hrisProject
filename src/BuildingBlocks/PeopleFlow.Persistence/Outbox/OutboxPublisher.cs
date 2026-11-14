using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;
using PeopleFlow.Security.Redaction;

namespace PeopleFlow.Persistence.Outbox;

public static class PlatformOutboxMessageTypes
{
    public const string BreakGlassLoginAlert = "Security.BreakGlassLoginAlert";
    public const string PasswordResetRequested = "Identity.PasswordResetRequested";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        BreakGlassLoginAlert,
        PasswordResetRequested
    };
}

public class OutboxPublisher : IOutboxPublisher
{
    private static readonly IReadOnlySet<string> PreservedOutboxProperties =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "protectedResetToken",
            "normalizedEmail",
            "invitedEmail"
        };

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
        var sanitizedPayload = PeopleFlow.Security.Redaction.SensitiveDataRedactor.SanitizeObjectGraph(message);
        var payloadJson = JsonSerializer.Serialize(sanitizedPayload);

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

        var ambientTenantId = _tenantContextAccessor?.TenantId ?? _dbContext.CurrentTenantId;
        var effectiveTenantId = tenantId ?? ambientTenantId;

        if (ambientTenantId.HasValue && effectiveTenantId != ambientTenantId.Value)
        {
            throw new UnauthorizedAccessException(
                $"Cannot publish outbox message for tenant '{effectiveTenantId}' while ambient tenant context is '{ambientTenantId.Value}'.");
        }

        if (!effectiveTenantId.HasValue)
        {
            return await PublishPlatformAsync(messageType, payloadJson, correlationId, cancellationToken);
        }

        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        var safePayloadJson = SensitiveDataRedactor.RedactJson(payloadJson, PreservedOutboxProperties);

        var outboxMessage = new OutboxMessage
        {
            TenantId = effectiveTenantId,
            MessageType = messageType,
            PayloadJson = safePayloadJson,
            CorrelationId = correlationId ?? Guid.CreateVersion7(),
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            NextAttemptAtUtc = now
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        if (autoSave)
        {
            using var scope = !ambientTenantId.HasValue && effectiveTenantId.HasValue
                ? _dbContext.BeginOperationTenantScope(effectiveTenantId.Value)
                : null;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return outboxMessage;
    }

    public async Task<OutboxMessage> PublishPlatformAsync(
        string messageType,
        string payloadJson,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageType) || !PlatformOutboxMessageTypes.Allowed.Contains(messageType))
        {
            throw new UnauthorizedAccessException($"Platform outbox message type '{messageType}' is not allowlisted.");
        }

        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        var safePayloadJson = SensitiveDataRedactor.RedactJson(payloadJson, PreservedOutboxProperties);
        var outboxMessage = new OutboxMessage(Guid.CreateVersion7())
        {
            TenantId = null,
            MessageType = messageType,
            PayloadJson = safePayloadJson,
            CorrelationId = correlationId ?? Guid.CreateVersion7(),
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            NextAttemptAtUtc = now
        };

        if (_dbContext.Database.IsNpgsql())
        {
            Guid? createdBy = null;
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT fn_enqueue_platform_outbox_message({outboxMessage.Id}, {outboxMessage.MessageType}, CAST({outboxMessage.PayloadJson} AS jsonb), {outboxMessage.CorrelationId}, {now}, {outboxMessage.NextAttemptAtUtc}, CAST({createdBy} AS uuid));",
                cancellationToken);
        }
        else
        {
            _dbContext.OutboxMessages.Add(outboxMessage);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return outboxMessage;
    }
}
