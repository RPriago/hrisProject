using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Jobs.Filters;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Persistence;
using PeopleFlow.Modules.Subscription.Core.Services;
using PeopleFlow.Persistence.Audit;

namespace PeopleFlow.Modules.Subscription.Core.Jobs;

[PlatformSystemJob]
public sealed class BillingWebhookRetryJob
{
    private const int MaxAttempts = 5;

    private readonly SubscriptionDbContext _db;
    private readonly BillingWebhookService _webhookService;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IClock _clock;

    public BillingWebhookRetryJob(
        SubscriptionDbContext db,
        BillingWebhookService webhookService,
        IPlatformAuditLogWriter auditWriter,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _webhookService = webhookService ?? throw new ArgumentNullException(nameof(webhookService));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var stalePendingCutoff = now.AddMinutes(-5);
        var retryStatus = WebhookProcessingStatus.RetryScheduled.ToString();

        var candidates = await _db.BillingWebhookEvents
            .Where(e => e.ProcessingStatus == retryStatus || e.ProcessingStatus == "Pending")
            .ToListAsync(cancellationToken);

        var pendingRetries = candidates
            .Where(e => e.ProcessingStatus == retryStatus || e.ReceivedAtUtc <= stalePendingCutoff)
            .ToList();

        if (pendingRetries.Count == 0)
        {
            return;
        }

        var reconciledAuditIds = new List<Guid>();

        foreach (var evt in pendingRetries)
        {
            evt.AttemptCount += 1;

            BillingWebhookService.WebhookEnvelopePayload? payload = null;
            try
            {
                payload = JsonSerializer.Deserialize<BillingWebhookService.WebhookEnvelopePayload>(
                    evt.PayloadJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException)
            {
                evt.ProcessingStatus = WebhookProcessingStatus.FailedNeedsReconciliation.ToString();
                evt.ErrorSummary = "Corrupted JSON payload during retry.";
                evt.ProcessedAtUtc = now;
                reconciledAuditIds.Add(evt.Id);
                continue;
            }

            if (payload is null)
            {
                evt.ProcessingStatus = WebhookProcessingStatus.FailedNeedsReconciliation.ToString();
                evt.ErrorSummary = "Empty webhook payload during retry.";
                evt.ProcessedAtUtc = now;
                reconciledAuditIds.Add(evt.Id);
                continue;
            }

            var status = await _webhookService.ApplyWebhookEventAsync(evt, payload, cancellationToken);
            if (status == WebhookProcessingStatus.RetryScheduled && evt.AttemptCount >= MaxAttempts)
            {
                evt.ProcessingStatus = WebhookProcessingStatus.FailedNeedsReconciliation.ToString();
                evt.ProcessedAtUtc = now;
                evt.ErrorSummary = $"{evt.ErrorSummary} (Max attempts reached)";
                reconciledAuditIds.Add(evt.Id);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var eventId in reconciledAuditIds)
        {
            await _auditWriter.WriteAsync(new PlatformAuditEntry(
                null,
                "SystemJob",
                "BILLING_WEBHOOK_FAILED_NEEDS_RECONCILIATION",
                ResourcePath: nameof(BillingWebhookRetryJob),
                MetadataJson: JsonSerializer.Serialize(new { eventId })), cancellationToken);
        }
    }
}
