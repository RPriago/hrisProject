using PeopleFlow.Jobs.Envelopes;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Jobs.Tracker;

/// <summary>
/// Status state machine constants for background job runs.
/// </summary>
public static class JobRunStatus
{
    public const string Queued = "Queued";
    public const string Validating = "Validating";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string CompletedWithErrors = "CompletedWithErrors";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// Tracks the lifecycle and item progress of asynchronous background jobs on infra_background_job_runs.
/// </summary>
public interface IJobProgressTracker
{
    Task<BackgroundJobRun> EnqueueAsync(
        Guid tenantId,
        string jobCategory,
        Guid correlationId,
        Guid? initiatedByUserId = null,
        Guid? referenceEntityId = null,
        int? totalItems = null,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> EnqueueAsync(
        Guid tenantId,
        string jobCategory,
        string correlationId,
        Guid? initiatedByUserId = null,
        Guid? referenceEntityId = null,
        int? totalItems = null,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> EnqueueAsync(
        JobEnvelope envelope,
        string jobCategory,
        int? totalItems = null,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> AttachHangfireJobIdAsync(
        Guid jobRunId,
        string hangfireJobId,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> StartValidatingAsync(
        Guid jobRunId,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> StartProcessingAsync(
        Guid jobRunId,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> UpdateProgressAsync(
        Guid jobRunId,
        int succeededItems,
        int failedItems,
        int? totalItems = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> ReportProgressAsync(
        Guid jobRunId,
        int processedItems,
        int succeededItems,
        int failedItems,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> CompleteAsync(
        Guid jobRunId,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> CompleteWithErrorsAsync(
        Guid jobRunId,
        string? errorDetails = null,
        Guid? errorReportFileId = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> FailAsync(
        Guid jobRunId,
        string errorDetails,
        Guid? errorReportFileId = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> CancelAsync(
        Guid jobRunId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun?> GetByIdAsync(
        Guid jobRunId,
        CancellationToken cancellationToken = default);
}
