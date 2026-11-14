using PeopleFlow.Kernel.Time;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Security.Context;
using PeopleFlow.Security.Redaction;

namespace PeopleFlow.Persistence.Audit;

public sealed class PlatformAuditLogWriter : IPlatformAuditLogWriter
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly ICurrentActorContext _actorContext;
    private readonly IClock _clock;

    public PlatformAuditLogWriter(
        PeopleFlowDbContext dbContext,
        ICurrentActorContext actorContext,
        IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _actorContext = actorContext ?? throw new ArgumentNullException(nameof(actorContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task WriteAsync(PlatformAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var safeReason = entry.Reason == null
            ? null
            : SensitiveDataRedactor.RedactSecretsFromText(entry.Reason);

        var record = new PlatformAuditLog(Guid.CreateVersion7())
        {
            ActorUserId = entry.ActorUserId ?? _actorContext.UserId,
            ActorRole = string.IsNullOrWhiteSpace(entry.ActorRole) ? "System" : entry.ActorRole,
            Action = entry.Action,
            TargetTenantId = entry.TargetTenantId,
            SupportGrantId = entry.SupportGrantId,
            ResourcePath = entry.ResourcePath,
            CorrelationId = entry.CorrelationId ?? _actorContext.CorrelationId,
            Reason = safeReason,
            MetadataJson = entry.MetadataJson == null ? null : SensitiveDataRedactor.RedactJson(entry.MetadataJson),
            OccurredAtUtc = _clock.UtcNow
        };

        _dbContext.PlatformAuditLogs.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
