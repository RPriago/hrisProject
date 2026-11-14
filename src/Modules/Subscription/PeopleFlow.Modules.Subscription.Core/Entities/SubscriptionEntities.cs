using PeopleFlow.Kernel.Domain;

namespace PeopleFlow.Modules.Subscription.Core.Entities;

public class SubscriptionPlan : BaseEntity, IPlatformOwnedEntity
{
    public SubscriptionPlan() { }
    public SubscriptionPlan(Guid id) : base(id) { }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EnabledModulesJson { get; set; } = "[]";
    public int MaxActiveEmployees { get; set; }
    public long MaxStorageBytes { get; set; }
    public int MaxApiClients { get; set; }
    public decimal MonthlyPriceAmount { get; set; }
    public string Currency { get; set; } = "IDR";
    public bool IsActive { get; set; } = true;
}

public class TenantSubscription : BaseEntity, IPlatformOwnedEntity
{
    public TenantSubscription() { }
    public TenantSubscription(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CurrentPeriodStartUtc { get; set; }
    public DateTimeOffset CurrentPeriodEndUtc { get; set; }
    public DateTimeOffset? TrialEndsAtUtc { get; set; }
    public DateTimeOffset? GracePeriodEndsAtUtc { get; set; }
    public DateTimeOffset? ReadOnlySinceUtc { get; set; }
    public int? OverrideMaxEmployees { get; set; }
    public long? OverrideMaxStorageBytes { get; set; }
}

public class SubscriptionInvoice : BaseEntity, IPlatformOwnedEntity
{
    public SubscriptionInvoice() { }
    public SubscriptionInvoice(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "IDR";
    public string Status { get; set; } = "Open";
    public string PaymentChannel { get; set; } = "SandboxWebhook";
    public string? PaymentReference { get; set; }
    public DateTimeOffset? PaidAtUtc { get; set; }
    public Guid? RecordedByAdminId { get; set; }
    public string? Notes { get; set; }
}

public class BillingWebhookEvent : BaseEntity, IPlatformOwnedEntity
{
    public BillingWebhookEvent() { }
    public BillingWebhookEvent(Guid id) : base(id) { }

    public string ProviderName { get; set; } = string.Empty;
    public string ProviderEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool SignatureVerified { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string ProcessingStatus { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}
