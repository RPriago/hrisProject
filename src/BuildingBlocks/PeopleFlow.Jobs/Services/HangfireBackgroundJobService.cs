using System.Linq.Expressions;
using Hangfire;
using Hangfire.Server;
using PeopleFlow.Jobs.Envelopes;
using PeopleFlow.Jobs.Tracker;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Jobs.Services;

/// <summary>
/// Hangfire implementation of IBackgroundJobService ensuring every scheduled job
/// strictly carries a valid JobEnvelope and records an entry via IJobProgressTracker.
/// </summary>
public sealed class HangfireBackgroundJobService : IBackgroundJobService
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IJobProgressTracker _jobProgressTracker;

    public HangfireBackgroundJobService(
        IBackgroundJobClient backgroundJobClient,
        IJobProgressTracker jobProgressTracker)
    {
        _backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
        _jobProgressTracker = jobProgressTracker ?? throw new ArgumentNullException(nameof(jobProgressTracker));
    }

    public async Task<BackgroundJobRun> EnqueueAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Func<TJob, Task>> methodCall,
        int? totalItems = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobCategory);
        ArgumentNullException.ThrowIfNull(methodCall);

        ValidateMethodCallExpression(methodCall, envelope);

        var run = await _jobProgressTracker.EnqueueAsync(envelope, jobCategory, totalItems, hangfireJobId: null, cancellationToken);
        try
        {
            var jobId = _backgroundJobClient.Enqueue(methodCall);
            if (!string.IsNullOrEmpty(jobId))
            {
                await _jobProgressTracker.AttachHangfireJobIdAsync(run.Id, jobId, cancellationToken);
                run.HangfireJobId = jobId;
            }
            return run;
        }
        catch (Exception ex)
        {
            await _jobProgressTracker.FailAsync(run.Id, ex.Message, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<BackgroundJobRun> EnqueueAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Action<TJob>> methodCall,
        int? totalItems = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobCategory);
        ArgumentNullException.ThrowIfNull(methodCall);

        ValidateMethodCallExpression(methodCall, envelope);

        var run = await _jobProgressTracker.EnqueueAsync(envelope, jobCategory, totalItems, hangfireJobId: null, cancellationToken);
        try
        {
            var jobId = _backgroundJobClient.Enqueue(methodCall);
            if (!string.IsNullOrEmpty(jobId))
            {
                await _jobProgressTracker.AttachHangfireJobIdAsync(run.Id, jobId, cancellationToken);
                run.HangfireJobId = jobId;
            }
            return run;
        }
        catch (Exception ex)
        {
            await _jobProgressTracker.FailAsync(run.Id, ex.Message, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<BackgroundJobRun> ScheduleAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Func<TJob, Task>> methodCall,
        TimeSpan delay,
        int? totalItems = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobCategory);
        ArgumentNullException.ThrowIfNull(methodCall);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay must not be negative.");
        }

        ValidateMethodCallExpression(methodCall, envelope);

        var run = await _jobProgressTracker.EnqueueAsync(envelope, jobCategory, totalItems, hangfireJobId: null, cancellationToken);
        try
        {
            var jobId = _backgroundJobClient.Schedule(methodCall, delay);
            if (!string.IsNullOrEmpty(jobId))
            {
                await _jobProgressTracker.AttachHangfireJobIdAsync(run.Id, jobId, cancellationToken);
                run.HangfireJobId = jobId;
            }
            return run;
        }
        catch (Exception ex)
        {
            await _jobProgressTracker.FailAsync(run.Id, ex.Message, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<BackgroundJobRun> ScheduleAsync<TJob>(
        JobEnvelope envelope,
        string jobCategory,
        Expression<Action<TJob>> methodCall,
        TimeSpan delay,
        int? totalItems = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobCategory);
        ArgumentNullException.ThrowIfNull(methodCall);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay must not be negative.");
        }

        ValidateMethodCallExpression(methodCall, envelope);

        var run = await _jobProgressTracker.EnqueueAsync(envelope, jobCategory, totalItems, hangfireJobId: null, cancellationToken);
        try
        {
            var jobId = _backgroundJobClient.Schedule(methodCall, delay);
            if (!string.IsNullOrEmpty(jobId))
            {
                await _jobProgressTracker.AttachHangfireJobIdAsync(run.Id, jobId, cancellationToken);
                run.HangfireJobId = jobId;
            }
            return run;
        }
        catch (Exception ex)
        {
            await _jobProgressTracker.FailAsync(run.Id, ex.Message, cancellationToken: cancellationToken);
            throw;
        }
    }

    private static void ValidateMethodCallExpression(LambdaExpression expression, JobEnvelope expectedEnvelope)
    {
        if (expression.Body is not MethodCallExpression call)
        {
            throw new InvalidOperationException("Method call expression is required.");
        }

        JobEnvelope? envelopeFound = null;

        foreach (var argument in call.Arguments)
        {
            var evaluated = EvaluateExpression(argument);

            if (evaluated is JobEnvelope env)
            {
                if (envelopeFound != null)
                {
                    throw new InvalidOperationException("Only one JobEnvelope argument is allowed in background job method.");
                }

                if (env.TenantId != expectedEnvelope.TenantId || env.CorrelationId != expectedEnvelope.CorrelationId)
                {
                    throw new InvalidOperationException("Job argument JobEnvelope does not match the supplied JobEnvelope.");
                }

                envelopeFound = env;
            }
            else if (evaluated is CancellationToken || evaluated is IJobCancellationToken || evaluated is PerformContext
                     || typeof(CancellationToken).IsAssignableFrom(argument.Type)
                     || typeof(IJobCancellationToken).IsAssignableFrom(argument.Type)
                     || typeof(PerformContext).IsAssignableFrom(argument.Type))
            {
                // Allowed framework tokens
            }
            else
            {
                throw new InvalidOperationException($"Job argument of type '{argument.Type.Name}' is not permitted. Only JobEnvelope and framework tokens are allowed.");
            }
        }

        if (envelopeFound == null)
        {
            throw new InvalidOperationException("Background job method must take exactly one JobEnvelope argument matching the supplied envelope.");
        }
    }

    private static object? EvaluateExpression(Expression argument)
    {
        if (argument is ConstantExpression constant)
        {
            return constant.Value;
        }

        try
        {
            var lambda = Expression.Lambda(argument);
            return lambda.Compile().DynamicInvoke();
        }
        catch
        {
            return null;
        }
    }
}
