using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Jobs.Filters;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Persistence;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Outbox;

namespace PeopleFlow.Modules.Subscription.Core.Jobs;

[PlatformSystemJob]
public sealed class SubscriptionLifecycleSweepJob
{
    private readonly SubscriptionDbContext _db;
    private readonly IOutboxPublisher _outbox;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IClock _clock;

    public SubscriptionLifecycleSweepJob(
        SubscriptionDbContext db,
        IOutboxPublisher outbox,
        IPlatformAuditLogWriter auditWriter,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var candidates = await _db.TenantSubscriptions
            .Where(s => s.Status == SubscriptionStatusConstants.Trial
                     || s.Status == SubscriptionStatusConstants.Active
                     || s.Status == SubscriptionStatusConstants.PastDue)
            .ToListAsync(cancellationToken);

        var notifications = new List<(Guid TenantId, string MessageType, string AuditAction)>();

        foreach (var sub in candidates)
        {
            if (sub.Status == SubscriptionStatusConstants.Trial &&
                sub.TrialEndsAtUtc.HasValue &&
                sub.TrialEndsAtUtc.Value <= now)
            {
                sub.Status = SubscriptionStatusConstants.Suspended;
                notifications.Add((sub.TenantId, "Subscription.TrialExpiredSuspended", "SUBSCRIPTION_TRIAL_SUSPENDED"));
                continue;
            }

            if (sub.Status == SubscriptionStatusConstants.Active &&
                sub.CurrentPeriodEndUtc <= now)
            {
                sub.Status = SubscriptionStatusConstants.PastDue;
                sub.GracePeriodEndsAtUtc ??= now.AddDays(7);
                notifications.Add((sub.TenantId, "Subscription.EnteredPastDue", "SUBSCRIPTION_ENTERED_PAST_DUE"));
                continue;
            }

            if (sub.Status == SubscriptionStatusConstants.PastDue &&
                sub.GracePeriodEndsAtUtc.HasValue &&
                sub.GracePeriodEndsAtUtc.Value <= now)
            {
                if (!sub.ReadOnlySinceUtc.HasValue)
                {
                    sub.ReadOnlySinceUtc = now;
                    notifications.Add((sub.TenantId, "Subscription.EnteredReadOnlyGrace", "SUBSCRIPTION_ENTERED_READ_ONLY"));
                }
                else if (now >= sub.ReadOnlySinceUtc.Value.AddDays(7))
                {
                    sub.Status = SubscriptionStatusConstants.Suspended;
                    notifications.Add((sub.TenantId, "Subscription.SuspendedNonPayment", "SUBSCRIPTION_SUSPENDED_NON_PAYMENT"));
                }
            }
        }

        if (notifications.Count == 0)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var (tenantId, messageType, auditAction) in notifications)
        {
            await _outbox.PublishRawAsync(
                messageType,
                JsonSerializer.Serialize(new { tenantId, occurredAtUtc = now }),
                tenantId: tenantId,
                autoSave: true,
                cancellationToken: cancellationToken);

            await _auditWriter.WriteAsync(new PlatformAuditEntry(
                null,
                "SystemJob",
                auditAction,
                TargetTenantId: tenantId,
                ResourcePath: nameof(SubscriptionLifecycleSweepJob)), cancellationToken);
        }
    }
}
