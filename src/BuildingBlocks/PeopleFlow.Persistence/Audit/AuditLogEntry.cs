namespace PeopleFlow.Persistence.Audit;

/// <summary>
/// Standardized 12-property audit log entry captured atomically across modules.
/// </summary>
public sealed record AuditLogEntry
{
    public Guid TenantId { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid? SessionId { get; init; }
    public Guid CorrelationId { get; init; } = Guid.CreateVersion7();
    public string Module { get; init; } = "Core";
    public string Action { get; init; } = "Action";
    public string EntityType { get; init; } = "Entity";
    public string EntityId { get; init; } = Guid.Empty.ToString();
    public IReadOnlyDictionary<string, object?>? OldValues { get; init; }
    public IReadOnlyDictionary<string, object?>? NewValues { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string ActionType { get => Action; init => Action = value; }
    public string EntityName { get => EntityType; init => EntityType = value; }
    public IReadOnlyDictionary<string, object?>? AfterValues { get => NewValues; init => NewValues = value; }
    public IReadOnlyDictionary<string, object?>? BeforeValues { get => OldValues; init => OldValues = value; }

    public AuditLogEntry()
    {
    }

    public AuditLogEntry(
        Guid TenantId,
        Guid? ActorUserId,
        Guid? SessionId,
        Guid CorrelationId,
        string Module,
        string Action,
        string EntityType,
        string EntityId,
        IReadOnlyDictionary<string, object?>? OldValues = null,
        IReadOnlyDictionary<string, object?>? NewValues = null,
        string? Reason = null,
        DateTimeOffset? OccurredAtUtc = null)
    {
        if (TenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(TenantId));
        if (CorrelationId == Guid.Empty)
            throw new ArgumentException("CorrelationId must not be empty.", nameof(CorrelationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(EntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(EntityId);

        this.TenantId = TenantId;
        this.ActorUserId = ActorUserId;
        this.SessionId = SessionId;
        this.CorrelationId = CorrelationId;
        this.Module = Module;
        this.Action = Action;
        this.EntityType = EntityType;
        this.EntityId = EntityId;
        this.OldValues = OldValues;
        this.NewValues = NewValues;
        this.Reason = Reason;
        this.OccurredAtUtc = OccurredAtUtc;
    }

    public DateTimeOffset TimestampUtc => OccurredAtUtc ?? DateTimeOffset.UtcNow;
}
