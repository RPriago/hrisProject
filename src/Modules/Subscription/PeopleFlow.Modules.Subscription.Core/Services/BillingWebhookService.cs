using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Entities;
using PeopleFlow.Modules.Subscription.Core.Persistence;

namespace PeopleFlow.Modules.Subscription.Core.Services;

public sealed class BillingWebhookService : IBillingWebhookService
{
    private readonly SubscriptionDbContext _dbContext;
    private readonly IClock _clock;
    private readonly BillingOptions _options;

    public BillingWebhookService(
        SubscriptionDbContext dbContext,
        IClock clock,
        IOptions<BillingOptions> options)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<Result<WebhookProcessingResult>> ProcessWebhookAsync(
        string provider,
        string rawBody,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(rawBody))
        {
            return Result.Failure<WebhookProcessingResult>(Error.Validation("WEBHOOK_INVALID_REQUEST", "Provider and payload are required."));
        }

        if (!_options.WebhookSecrets.TryGetValue(provider.Trim(), out var secret) || string.IsNullOrWhiteSpace(secret))
        {
            return Result.Failure<WebhookProcessingResult>(Error.Unauthorized("WEBHOOK_PROVIDER_UNCONFIGURED", $"Webhook secret is not configured for provider '{provider}'."));
        }

        var verificationResult = VerifySignatureHeader(signatureHeader, rawBody, secret, _clock.UtcNow, _options.MaxTimestampSkewSeconds);
        if (verificationResult.IsFailure)
        {
            return Result.Failure<WebhookProcessingResult>(verificationResult.Error);
        }

        WebhookEnvelopePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookEnvelopePayload>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Result.Failure<WebhookProcessingResult>(Error.Validation("WEBHOOK_INVALID_JSON", "Webhook body is not valid JSON."));
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.EventId) || string.IsNullOrWhiteSpace(payload.EventType))
        {
            return Result.Failure<WebhookProcessingResult>(Error.Validation("WEBHOOK_MISSING_FIELDS", "Webhook payload must contain eventId and eventType."));
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var trimmedEventId = payload.EventId.Trim();
        var existing = await _dbContext.BillingWebhookEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.ProviderName == normalizedProvider && e.ProviderEventId == trimmedEventId, cancellationToken);
        if (existing != null)
        {
            return Result.Success(new WebhookProcessingResult(existing.Id, WebhookProcessingStatus.IgnoredDuplicate, "Duplicate webhook event ignored."));
        }

        var now = _clock.UtcNow;
        var eventRecord = new BillingWebhookEvent(Guid.CreateVersion7())
        {
            ProviderName = normalizedProvider,
            ProviderEventId = trimmedEventId,
            EventType = payload.EventType.Trim(),
            SignatureVerified = true,
            PayloadJson = rawBody,
            ProcessingStatus = "Pending",
            AttemptCount = 1,
            ReceivedAtUtc = now
        };

        var ownTx = _dbContext.Database.CurrentTransaction is null;
        await using var tx = ownTx ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

        _dbContext.BillingWebhookEvents.Add(eventRecord);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
            }
            _dbContext.Entry(eventRecord).State = EntityState.Detached;
            return Result.Success(new WebhookProcessingResult(Guid.Empty, WebhookProcessingStatus.IgnoredDuplicate, "Concurrent duplicate webhook event ignored."));
        }

        var applyOutcome = await ApplyWebhookEventAsync(eventRecord, payload, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return Result.Success(new WebhookProcessingResult(eventRecord.Id, applyOutcome, $"Webhook processed with status {applyOutcome}."));
    }

    public async Task<WebhookProcessingStatus> ApplyWebhookEventAsync(
        BillingWebhookEvent eventRecord,
        WebhookEnvelopePayload payload,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        if (!payload.TenantId.HasValue || payload.TenantId.Value == Guid.Empty)
        {
            eventRecord.ProcessingStatus = WebhookProcessingStatus.FailedNeedsReconciliation.ToString();
            eventRecord.ErrorSummary = "Missing valid tenantId in billing webhook payload.";
            eventRecord.ProcessedAtUtc = now;
            return WebhookProcessingStatus.FailedNeedsReconciliation;
        }

        var subscription = await _dbContext.TenantSubscriptions
            .SingleOrDefaultAsync(s => s.TenantId == payload.TenantId.Value, cancellationToken);
        if (subscription == null)
        {
            eventRecord.ProcessingStatus = WebhookProcessingStatus.RetryScheduled.ToString();
            eventRecord.ErrorSummary = $"Tenant subscription '{payload.TenantId}' not found yet.";
            return WebhookProcessingStatus.RetryScheduled;
        }

        // Reject stale out-of-order events using UpdatedAtUtc (or CreatedAtUtc on first update) as watermark
        var watermark = subscription.UpdatedAtUtc ?? subscription.CreatedAtUtc;
        if (payload.OccurredAtUtc.HasValue && payload.OccurredAtUtc.Value < watermark)
        {
            eventRecord.ProcessingStatus = WebhookProcessingStatus.IgnoredOutOfOrder.ToString();
            eventRecord.ProcessedAtUtc = now;
            return WebhookProcessingStatus.IgnoredOutOfOrder;
        }

        if (string.Equals(payload.EventType, "invoice.paid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payload.EventType, "subscription.activated", StringComparison.OrdinalIgnoreCase))
        {
            if (!SubscriptionLifecycleService.CanTransition(subscription.Status, SubscriptionStatusConstants.Active))
            {
                eventRecord.ProcessingStatus = WebhookProcessingStatus.FailedNeedsReconciliation.ToString();
                eventRecord.ErrorSummary = $"Cannot transition subscription from '{subscription.Status}' to '{SubscriptionStatusConstants.Active}' via webhook.";
                eventRecord.ProcessedAtUtc = now;
                return WebhookProcessingStatus.FailedNeedsReconciliation;
            }

            subscription.Status = SubscriptionStatusConstants.Active;
            subscription.GracePeriodEndsAtUtc = null;
            subscription.ReadOnlySinceUtc = null;
            subscription.TrialEndsAtUtc = null;
            var baseStart = subscription.CurrentPeriodEndUtc > now ? subscription.CurrentPeriodEndUtc : now;
            subscription.CurrentPeriodStartUtc = now;
            subscription.CurrentPeriodEndUtc = baseStart.AddMonths(1);
            subscription.MarkUpdated(now);

            var trimmedInvoiceNumber = payload.InvoiceNumber?.Trim();
            if (!string.IsNullOrEmpty(trimmedInvoiceNumber))
            {
                var invoiceExists = await _dbContext.SubscriptionInvoices
                    .AnyAsync(i => i.InvoiceNumber == trimmedInvoiceNumber, cancellationToken);
                if (!invoiceExists)
                {
                    _dbContext.SubscriptionInvoices.Add(new SubscriptionInvoice(Guid.CreateVersion7())
                    {
                        TenantId = payload.TenantId.Value,
                        InvoiceNumber = trimmedInvoiceNumber,
                        Amount = payload.Amount ?? 0m,
                        Currency = (payload.Currency ?? "IDR").ToUpperInvariant(),
                        Status = "Paid",
                        PaymentChannel = "SandboxWebhook",
                        PaymentReference = payload.EventId.Trim(),
                        PaidAtUtc = payload.OccurredAtUtc ?? now
                    });
                }
            }
        }
        else if (string.Equals(payload.EventType, "invoice.payment_failed", StringComparison.OrdinalIgnoreCase))
        {
            if (!SubscriptionLifecycleService.CanTransition(subscription.Status, SubscriptionStatusConstants.PastDue))
            {
                eventRecord.ProcessingStatus = WebhookProcessingStatus.FailedNeedsReconciliation.ToString();
                eventRecord.ErrorSummary = $"Cannot transition subscription from '{subscription.Status}' to '{SubscriptionStatusConstants.PastDue}' via webhook.";
                eventRecord.ProcessedAtUtc = now;
                return WebhookProcessingStatus.FailedNeedsReconciliation;
            }

            subscription.Status = SubscriptionStatusConstants.PastDue;
            subscription.GracePeriodEndsAtUtc ??= now.AddDays(7);
            subscription.MarkUpdated(now);
        }
        else if (string.Equals(payload.EventType, "subscription.cancelled", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(payload.EventType, "subscription.canceled", StringComparison.OrdinalIgnoreCase))
        {
            if (!SubscriptionLifecycleService.CanTransition(subscription.Status, SubscriptionStatusConstants.Cancelled))
            {
                eventRecord.ProcessingStatus = WebhookProcessingStatus.FailedNeedsReconciliation.ToString();
                eventRecord.ErrorSummary = $"Cannot transition subscription from '{subscription.Status}' to '{SubscriptionStatusConstants.Cancelled}' via webhook.";
                eventRecord.ProcessedAtUtc = now;
                return WebhookProcessingStatus.FailedNeedsReconciliation;
            }

            subscription.Status = SubscriptionStatusConstants.Cancelled;
            subscription.MarkUpdated(now);
        }

        eventRecord.ProcessingStatus = WebhookProcessingStatus.Processed.ToString();
        eventRecord.ErrorSummary = null;
        eventRecord.ProcessedAtUtc = now;
        return WebhookProcessingStatus.Processed;
    }

    private static Result VerifySignatureHeader(
        string? signatureHeader,
        string rawBody,
        string secret,
        DateTimeOffset utcNow,
        int maxSkewSeconds)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return Result.Failure(Error.Unauthorized("WEBHOOK_SIGNATURE_MISSING", "Missing X-Billing-Signature header."));
        }

        long? timestamp = null;
        string? sigHex = null;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("t=", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(part[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTs))
            {
                timestamp = parsedTs;
            }
            else if (part.StartsWith("v1=", StringComparison.OrdinalIgnoreCase))
            {
                sigHex = part[3..].Trim();
            }
        }

        if (!timestamp.HasValue || string.IsNullOrWhiteSpace(sigHex))
        {
            return Result.Failure(Error.Unauthorized("WEBHOOK_SIGNATURE_INVALID", "Malformed X-Billing-Signature header."));
        }

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(sigHex);
        }
        catch (FormatException)
        {
            return Result.Failure(Error.Unauthorized("WEBHOOK_SIGNATURE_INVALID", "Signature hex format is invalid."));
        }

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp.Value}.{rawBody}");
        using var hmac = new HMACSHA256(keyBytes);
        var expectedBytes = hmac.ComputeHash(signedPayload);

        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return Result.Failure(Error.Unauthorized("WEBHOOK_SIGNATURE_INVALID", "Webhook HMAC signature verification failed."));
        }

        var skew = Math.Abs(utcNow.ToUnixTimeSeconds() - timestamp.Value);
        if (skew > maxSkewSeconds)
        {
            return Result.Failure(Error.Unauthorized("WEBHOOK_TIMESTAMP_EXPIRED", "Webhook timestamp outside allowed anti-replay window."));
        }

        return Result.Success();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            return true;
        }

        return ex.InnerException?.Message?.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
    }

    public sealed record WebhookEnvelopePayload(
        string EventId,
        string EventType,
        Guid? TenantId,
        string? InvoiceNumber = null,
        decimal? Amount = null,
        string? Currency = null,
        DateTimeOffset? OccurredAtUtc = null);
}
