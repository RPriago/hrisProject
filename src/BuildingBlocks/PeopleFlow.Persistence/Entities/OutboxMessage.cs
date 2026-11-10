using System.ComponentModel.DataAnnotations.Schema;
using PeopleFlow.Kernel.Domain;

namespace PeopleFlow.Persistence.Entities;

/// <summary>
/// Infrastructure record for transactional outbox pattern post-commit messaging.
/// Maps to table 'infra_outbox_messages'.
/// </summary>
public class OutboxMessage : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? LastErrorSummary { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    // Convenience aliases
    [NotMapped]
    public string EventType
    {
        get => MessageType;
        set => MessageType = value;
    }

    [NotMapped]
    public int RetryCount
    {
        get => AttemptCount;
        set => AttemptCount = value;
    }

    [NotMapped]
    public string? LastError
    {
        get => LastErrorSummary;
        set => LastErrorSummary = value;
    }

    public OutboxMessage() : base()
    {
    }

    public OutboxMessage(Guid id) : base(id)
    {
    }
}
