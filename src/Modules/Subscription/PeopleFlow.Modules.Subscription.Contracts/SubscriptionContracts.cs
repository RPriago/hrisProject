using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Modules.Subscription.Contracts;

public static class SubscriptionStatusConstants
{
    public const string Trial = "Trial";
    public const string Active = "Active";
    public const string PastDue = "PastDue";
    public const string Suspended = "Suspended";
    public const string Cancelled = "Cancelled";
}

public sealed record TenantSubscriptionSnapshot(
    Guid TenantId,
    string PlanCode,
    string PlanName,
    string Status,
    bool IsReadOnlyMode,
    bool IsGracePeriodWarning,
    IReadOnlySet<string> ActiveModules,
    int MaxActiveEmployees,
    long MaxStorageBytes,
    int MaxApiClients,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? GracePeriodEndsAtUtc,
    DateTimeOffset? ReadOnlySinceUtc);

public interface ITenantSubscriptionQueryService
{
    Task<TenantSubscriptionSnapshot?> GetSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface IActiveApiClientCounter
{
    Task<int> CountActiveApiClientsAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPlanDto(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<string> EnabledModules,
    int MaxActiveEmployees,
    long MaxStorageBytes,
    int MaxApiClients,
    decimal MonthlyPriceAmount,
    string Currency,
    bool IsActive);

public sealed record SubscriptionInvoiceDto(
    Guid Id,
    string InvoiceNumber,
    decimal Amount,
    string Currency,
    string Status,
    string PaymentChannel,
    string? PaymentReference,
    DateTimeOffset? PaidAtUtc,
    string? Notes);

public interface ISubscriptionLifecycleService
{
    Task<Result> ValidateActivePlanAsync(string planCode, CancellationToken cancellationToken = default);
    Task CompensateInitialProvisionAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Result<TenantSubscriptionSnapshot>> ProvisionInitialAsync(Guid tenantId, string planCode, bool startAsTrial, CancellationToken cancellationToken = default);
    Task<Result<TenantSubscriptionSnapshot>> ChangePlanAsync(Guid tenantId, string targetPlanCode, int currentActiveEmployees, CancellationToken cancellationToken = default);
    Task<Result> UpdateOverridesAsync(Guid tenantId, int? overrideMaxEmployees, long? overrideMaxStorageBytes, CancellationToken cancellationToken = default);
    Task<Result<SubscriptionInvoiceDto>> RecordManualPaymentAsync(Guid tenantId, decimal amount, string currency, string paymentReference, Guid adminUserId, string reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionInvoiceDto>> ListInvoicesAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public enum WebhookProcessingStatus
{
    Processed = 1,
    IgnoredDuplicate = 2,
    IgnoredOutOfOrder = 3,
    RetryScheduled = 4,
    FailedNeedsReconciliation = 5
}

public sealed record WebhookProcessingResult(Guid EventRecordId, WebhookProcessingStatus Status, string Message);

public interface IBillingWebhookService
{
    Task<Result<WebhookProcessingResult>> ProcessWebhookAsync(
        string provider,
        string rawBody,
        string? signatureHeader,
        CancellationToken cancellationToken = default);
}
