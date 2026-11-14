using PeopleFlow.Kernel.Domain;

namespace PeopleFlow.Persistence.Audit;

/// <summary>
/// Append-only audit log for platform-level security events (Break-Glass logins, Support Access Grant sessions,
/// manual billing payments, and tenant lifecycle transitions).
/// </summary>
public class PlatformAuditLog : BaseEntity, IPlatformOwnedEntity
{
    public PlatformAuditLog() { }

    public PlatformAuditLog(Guid id) : base(id) { }

    public Guid? ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Guid? TargetTenantId { get; set; }
    public Guid? SupportGrantId { get; set; }
    public string ResourcePath { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
