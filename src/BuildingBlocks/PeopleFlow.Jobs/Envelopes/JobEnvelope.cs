using System.Text.Json.Serialization;

namespace PeopleFlow.Jobs.Envelopes;

/// <summary>
/// Immutable payload envelope for Hangfire background jobs.
/// Hangfire jobs MUST ONLY accept JobEnvelope as payload argument to prevent secret/PII leaks.
/// </summary>
public sealed record JobEnvelope
{
    public Guid TenantId { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid TargetEntityId { get; init; }
    public int ExpectedVersion { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }

    [JsonConstructor]
    public JobEnvelope(
        Guid tenantId,
        Guid? actorUserId,
        Guid correlationId,
        Guid targetEntityId,
        int expectedVersion = 0,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId must not be empty.", nameof(correlationId));
        if (targetEntityId == Guid.Empty)
            throw new ArgumentException("TargetEntityId must not be empty.", nameof(targetEntityId));
        if (expectedVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), "ExpectedVersion must be non-negative.");

        TenantId = tenantId;
        ActorUserId = actorUserId;
        CorrelationId = correlationId;
        TargetEntityId = targetEntityId;
        ExpectedVersion = expectedVersion;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public static JobEnvelope Create(
        Guid tenantId,
        Guid targetEntityId,
        int expectedVersion = 0,
        Guid? actorUserId = null,
        Guid? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new JobEnvelope(
            tenantId,
            actorUserId,
            correlationId ?? Guid.CreateVersion7(),
            targetEntityId,
            expectedVersion,
            metadata);
    }
}
