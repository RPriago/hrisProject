using System.ComponentModel.DataAnnotations.Schema;
using PeopleFlow.Kernel.Domain;

namespace PeopleFlow.Persistence.Entities;

/// <summary>
/// Infrastructure record for tracking asynchronous background job runs and import/export progress.
/// Maps to table 'infra_background_job_runs'.
/// </summary>
public class BackgroundJobRun : BaseEntity
{
    public Guid TenantId { get; set; }
    public string JobCategory { get; set; } = string.Empty;
    public Guid? ReferenceEntityId { get; set; }
    public string? HangfireJobId { get; set; }
    public Guid? InitiatedByUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Status { get; set; } = "Queued";
    public int? TotalItems { get; set; }
    public int SucceededItems { get; set; }
    public int FailedItems { get; set; }
    public Guid? ErrorReportFileId { get; set; }
    public StoredFile? ErrorReportFile { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ErrorDetails { get; set; }

    // Convenience aliases
    [NotMapped]
    public string JobName
    {
        get => JobCategory;
        set => JobCategory = value;
    }

    private int? _explicitProgressPercentage;

    [NotMapped]
    public int ProgressPercentage
    {
        get
        {
            if (_explicitProgressPercentage.HasValue) return _explicitProgressPercentage.Value;
            if (!TotalItems.HasValue || TotalItems.Value <= 0) return 0;
            var processed = SucceededItems + FailedItems;
            return Math.Min(100, (int)Math.Round((double)processed * 100 / TotalItems.Value));
        }
        set => _explicitProgressPercentage = value;
    }

    public BackgroundJobRun() : base()
    {
    }

    public BackgroundJobRun(Guid id) : base(id)
    {
    }

    public void MarkValidating(string? hangfireJobId = null, DateTimeOffset? nowUtc = null)
    {
        if (Status != "Queued" && Status != "Validating")
        {
            throw new InvalidOperationException($"Cannot transition job run from '{Status}' to 'Validating'.");
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        Status = "Validating";
        if (!string.IsNullOrWhiteSpace(hangfireJobId))
        {
            HangfireJobId = hangfireJobId;
        }
        StartedAtUtc ??= now;
        UpdatedAtUtc = now;
    }

    public void MarkProcessing(string? hangfireJobId = null, DateTimeOffset? nowUtc = null)
    {
        if (Status != "Queued" && Status != "Validating" && Status != "Processing")
        {
            throw new InvalidOperationException($"Cannot transition job run from '{Status}' to 'Processing'.");
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        Status = "Processing";
        if (!string.IsNullOrWhiteSpace(hangfireJobId))
        {
            HangfireJobId = hangfireJobId;
        }
        StartedAtUtc ??= now;
        UpdatedAtUtc = now;
    }

    public void MarkCompletedWithErrors(string? errorDetails = null, Guid? errorReportFileId = null, DateTimeOffset? nowUtc = null)
    {
        if (Status == "Failed" || Status == "Cancelled")
        {
            throw new InvalidOperationException($"Cannot complete a job run that is in '{Status}' status.");
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        Status = "CompletedWithErrors";
        ProgressPercentage = 100;
        if (!string.IsNullOrWhiteSpace(errorDetails))
        {
            ErrorDetails = errorDetails;
        }
        if (errorReportFileId.HasValue)
        {
            ErrorReportFileId = errorReportFileId.Value;
        }
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void MarkCancelled(string? reason = null, DateTimeOffset? nowUtc = null)
    {
        if (Status == "Completed" || Status == "CompletedWithErrors")
        {
            throw new InvalidOperationException($"Cannot cancel a job run that is already '{Status}'.");
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        Status = "Cancelled";
        if (!string.IsNullOrWhiteSpace(reason))
        {
            ErrorDetails = reason;
        }
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }
}
