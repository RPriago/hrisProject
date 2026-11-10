using System.Linq.Expressions;
using PeopleFlow.Jobs.Envelopes;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Jobs.Services;

/// <summary>
/// Interface for background job handlers accepting strictly typed JobEnvelope.
/// </summary>
public interface IBackgroundJob
{
    Task ExecuteAsync(JobEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Schedules and enqueues background jobs with mandatory JobEnvelope encapsulation,
/// tracking execution runs via IJobProgressTracker.
/// </summary>
public interface IBackgroundJobService
{
    Task<BackgroundJobRun> EnqueueAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Func<TJob, Task>> methodCall,
        int? totalItems = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> EnqueueAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Action<TJob>> methodCall,
        int? totalItems = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> ScheduleAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Func<TJob, Task>> methodCall,
        TimeSpan delay,
        int? totalItems = null,
        CancellationToken cancellationToken = default);

    Task<BackgroundJobRun> ScheduleAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Action<TJob>> methodCall,
        TimeSpan delay,
        int? totalItems = null,
        CancellationToken cancellationToken = default);
}
