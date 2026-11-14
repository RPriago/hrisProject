namespace PeopleFlow.Persistence.Audit;

public sealed record PlatformAuditEntry(
    Guid? ActorUserId,
    string ActorRole,
    string Action,
    Guid? TargetTenantId = null,
    Guid? SupportGrantId = null,
    string ResourcePath = "",
    Guid? CorrelationId = null,
    string? Reason = null,
    string? MetadataJson = null);

public interface IPlatformAuditLogWriter
{
    Task WriteAsync(PlatformAuditEntry entry, CancellationToken cancellationToken = default);
}
