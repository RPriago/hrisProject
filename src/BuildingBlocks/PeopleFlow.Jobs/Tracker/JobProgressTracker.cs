using Microsoft.EntityFrameworkCore;
using PeopleFlow.Jobs.Envelopes;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Jobs.Tracker;

/// <summary>
/// EF Core implementation of IJobProgressTracker updating infra_background_job_runs.
/// </summary>
public sealed class JobProgressTracker : IJobProgressTracker
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly IClock? _clock;
    private readonly ITenantContextAccessor? _tenantContextAccessor;

    public JobProgressTracker(
        PeopleFlowDbContext dbContext,
        IClock? clock = null,
        ITenantContextAccessor? tenantContextAccessor = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public JobProgressTracker(
        PeopleFlowDbContext dbContext,
        ITenantContextAccessor tenantContextAccessor)
        : this(dbContext, null, tenantContextAccessor)
    {
    }

    public async Task<BackgroundJobRun> EnqueueAsync(
        Guid tenantId,
        string jobCategory,
        Guid correlationId,
        Guid? initiatedByUserId = null,
        Guid? referenceEntityId = null,
        int? totalItems = null,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId must not be empty.", nameof(correlationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(jobCategory);

        if (_dbContext.CurrentTenantId.HasValue && _dbContext.CurrentTenantId.Value != tenantId)
        {
            throw new InvalidOperationException("Cannot enqueue job for a different tenant than the active ambient tenant.");
        }

        using var tenantScope = _dbContext.BeginOperationTenantScope(tenantId);

        var run = new BackgroundJobRun
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            JobCategory = jobCategory,
            CorrelationId = correlationId,
            InitiatedByUserId = initiatedByUserId,
            ReferenceEntityId = referenceEntityId,
            TotalItems = totalItems,
            HangfireJobId = hangfireJobId,
            Status = "Queued",
            SucceededItems = 0,
            FailedItems = 0
        };

        _dbContext.BackgroundJobRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public Task<BackgroundJobRun> EnqueueAsync(
        Guid tenantId,
        string jobCategory,
        string correlationId,
        Guid? initiatedByUserId = null,
        Guid? referenceEntityId = null,
        int? totalItems = null,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default)
    {
        var corrGuid = Guid.TryParse(correlationId, out var parsed) ? parsed : Guid.CreateVersion7();
        return EnqueueAsync(tenantId, jobCategory, corrGuid, initiatedByUserId, referenceEntityId, totalItems, hangfireJobId, cancellationToken);
    }

    public Task<BackgroundJobRun> EnqueueAsync(
        JobEnvelope envelope,
        string jobCategory,
        int? totalItems = null,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return EnqueueAsync(
            tenantId: envelope.TenantId,
            jobCategory: jobCategory,
            correlationId: envelope.CorrelationId,
            initiatedByUserId: envelope.ActorUserId,
            referenceEntityId: envelope.TargetEntityId,
            totalItems: totalItems,
            hangfireJobId: hangfireJobId,
            cancellationToken: cancellationToken);
    }

    public async Task<BackgroundJobRun> AttachHangfireJobIdAsync(
        Guid jobRunId,
        string hangfireJobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hangfireJobId);
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        run.HangfireJobId = hangfireJobId;
        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    public async Task<BackgroundJobRun> StartValidatingAsync(
        Guid jobRunId,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default)
    {
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        run.MarkValidating(hangfireJobId, _clock?.UtcNow);

        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    public async Task<BackgroundJobRun> StartProcessingAsync(
        Guid jobRunId,
        string? hangfireJobId = null,
        CancellationToken cancellationToken = default)
    {
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        run.MarkProcessing(hangfireJobId, _clock?.UtcNow);

        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    public async Task<BackgroundJobRun> UpdateProgressAsync(
        Guid jobRunId,
        int succeededItems,
        int failedItems,
        int? totalItems = null,
        CancellationToken cancellationToken = default)
    {
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        if (run.Status != "Processing")
        {
            throw new InvalidOperationException($"Cannot update progress for background job run '{jobRunId}' with status '{run.Status}'. Expected status is 'Processing'.");
        }

        run.SucceededItems = Math.Max(0, succeededItems);
        run.FailedItems = Math.Max(0, failedItems);
        if (totalItems.HasValue)
        {
            run.TotalItems = totalItems.Value;
        }

        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    public Task<BackgroundJobRun> ReportProgressAsync(
        Guid jobRunId,
        int processedItems,
        int succeededItems,
        int failedItems,
        CancellationToken cancellationToken = default)
    {
        return UpdateProgressAsync(jobRunId, succeededItems, failedItems, cancellationToken: cancellationToken);
    }

    public async Task<BackgroundJobRun> CompleteAsync(
        Guid jobRunId,
        CancellationToken cancellationToken = default)
    {
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        if (run.Status != "Processing")
        {
            throw new InvalidOperationException($"Cannot complete background job run '{jobRunId}' with status '{run.Status}'. Expected status is 'Processing'.");
        }

        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        run.Status = "Completed";
        run.CompletedAtUtc = now;

        // Ensure 100% completion if total was specified
        if (run.TotalItems.HasValue && run.TotalItems.Value > 0)
        {
            if (run.SucceededItems + run.FailedItems < run.TotalItems.Value)
            {
                run.SucceededItems = run.TotalItems.Value - run.FailedItems;
            }
        }

        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    public async Task<BackgroundJobRun> FailAsync(
        Guid jobRunId,
        string errorDetails,
        Guid? errorReportFileId = null,
        CancellationToken cancellationToken = default)
    {
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        if (run.Status is JobRunStatus.Completed or JobRunStatus.CompletedWithErrors or JobRunStatus.Failed or JobRunStatus.Cancelled)
        {
            throw new InvalidOperationException($"Cannot fail background job run '{jobRunId}' with terminal status '{run.Status}'.");
        }

        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        run.Status = JobRunStatus.Failed;
        run.StartedAtUtc ??= now;
        run.CompletedAtUtc = now;
        run.ErrorDetails = errorDetails;
        if (errorReportFileId.HasValue)
        {
            run.ErrorReportFileId = errorReportFileId;
        }

        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    public async Task<BackgroundJobRun> CompleteWithErrorsAsync(
        Guid jobRunId,
        string? errorDetails = null,
        Guid? errorReportFileId = null,
        CancellationToken cancellationToken = default)
    {
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        run.MarkCompletedWithErrors(errorDetails, errorReportFileId, _clock?.UtcNow);

        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    public async Task<BackgroundJobRun> CancelAsync(
        Guid jobRunId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var run = await GetRequiredRunAsync(jobRunId, cancellationToken);
        run.MarkCancelled(reason, _clock?.UtcNow);

        await SaveChangesInTenantScopeAsync(run.TenantId, cancellationToken);
        return run;
    }

    internal IDisposable BeginOperationTenantScope(Guid tenantId) => _dbContext.BeginOperationTenantScope(tenantId);

    public async Task<BackgroundJobRun?> GetByIdAsync(
        Guid jobRunId,
        CancellationToken cancellationToken = default)
    {
        var activeTenantId = _tenantContextAccessor?.TenantId ?? _dbContext.CurrentTenantId;
        if (!activeTenantId.HasValue || activeTenantId.Value == Guid.Empty)
        {
            return null;
        }

        using var tenantScope = _dbContext.BeginOperationTenantScope(activeTenantId.Value);
        return await _dbContext.BackgroundJobRuns.FirstOrDefaultAsync(r => r.Id == jobRunId, cancellationToken);
    }

    private async Task<BackgroundJobRun> GetRequiredRunAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        var activeTenantId = _tenantContextAccessor?.TenantId ?? _dbContext.CurrentTenantId;
        if (!activeTenantId.HasValue || activeTenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Active tenant context is required for background job run operations.");
        }

        using var tenantScope = _dbContext.BeginOperationTenantScope(activeTenantId.Value);
        var run = await _dbContext.BackgroundJobRuns.FirstOrDefaultAsync(r => r.Id == jobRunId, cancellationToken);

        if (run == null || run.TenantId != activeTenantId.Value)
        {
            throw new KeyNotFoundException($"Background job run '{jobRunId}' was not found.");
        }

        return run;
    }

    private async Task SaveChangesInTenantScopeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var tenantScope = _dbContext.BeginOperationTenantScope(tenantId);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
