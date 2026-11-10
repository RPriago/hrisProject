using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Persistence.Outbox;

public interface IOutboxMessageProcessor
{
    Task ProcessAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

public class OutboxDispatcherOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 50;
    public int MaxRetries { get; set; } = 5;
    public double BaseBackoffSeconds { get; set; } = 2.0;
}

public class OutboxDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;
    private readonly OutboxDispatcherOptions _options;
    private readonly IClock? _clock;

    public OutboxDispatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcherBackgroundService> logger,
        OutboxDispatcherOptions? options = null,
        IClock? clock = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new OutboxDispatcherOptions();
        _clock = clock;
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PeopleFlowDbContext>();
        var processors = scope.ServiceProvider.GetServices<IOutboxMessageProcessor>().ToList();
        var tenantAccessor = scope.ServiceProvider.GetService<ITenantContextAccessor>();

        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        var batchSize = _options.BatchSize;

        var isNpgsql = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL" &&
                       dbContext.Database.GetDbConnection().GetType().Name == "NpgsqlConnection";

        await using var transaction = isNpgsql
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        List<OutboxMessage> pendingMessages;

        if (isNpgsql)
        {
            await dbContext.Database.ExecuteSqlRawAsync("SELECT set_config('app.outbox_dispatcher_mode', 'true', true);", cancellationToken);

            pendingMessages = await dbContext.OutboxMessages
                .FromSqlRaw(
                    """
                    SELECT *, xmin FROM infra_outbox_messages 
                    WHERE status = 'Pending' AND next_attempt_at_utc <= {0} 
                    ORDER BY next_attempt_at_utc 
                    LIMIT {1} 
                    FOR UPDATE SKIP LOCKED
                    """, now, batchSize)
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }
        else
        {
            var pending = await dbContext.OutboxMessages
                .IgnoreQueryFilters()
                .Where(m => m.Status == OutboxMessageStatus.Pending)
                .ToListAsync(cancellationToken);

            pendingMessages = pending
                .Where(m => m.NextAttemptAtUtc <= now)
                .OrderBy(m => m.NextAttemptAtUtc)
                .Take(batchSize)
                .ToList();
        }

        if (pendingMessages.Count == 0)
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            return 0;
        }

        var processedCount = 0;

        foreach (var message in pendingMessages)
        {
            using var tenantScope = (message.TenantId.HasValue && tenantAccessor != null)
                ? tenantAccessor.BeginScope(new TenantContext(message.TenantId.Value, "Active"))
                : null;

            message.AttemptCount++;

            try
            {
                foreach (var processor in processors)
                {
                    await processor.ProcessAsync(message, cancellationToken);
                }

                message.Status = OutboxMessageStatus.Processed;
                message.ProcessedAtUtc = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
                message.LastErrorSummary = null;
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process outbox message {MessageId} of type {MessageType} (Attempt {AttemptCount})",
                    message.Id,
                    message.MessageType,
                    message.AttemptCount);

                var errorMsg = ex.ToString();
                message.LastErrorSummary = errorMsg.Length > 1000 ? errorMsg[..1000] : errorMsg;

                if (message.AttemptCount >= _options.MaxRetries)
                {
                    message.Status = OutboxMessageStatus.DeadLetter;
                    _logger.LogWarning(
                        "Outbox message {MessageId} reached maximum retry limit ({MaxRetries}) and transitioned to DeadLetter",
                        message.Id,
                        _options.MaxRetries);
                }
                else
                {
                    message.Status = OutboxMessageStatus.Pending;
                    var delaySeconds = _options.BaseBackoffSeconds * Math.Pow(2, message.AttemptCount - 1);
                    message.NextAttemptAtUtc = (_clock?.UtcNow ?? DateTimeOffset.UtcNow).AddSeconds(delaySeconds);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return processedCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcherBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;

            try
            {
                processedCount = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in outbox dispatcher background loop.");
            }

            // Eager dispatch: if batch was saturated (reached BatchSize), continue immediately to drain queue
            if (processedCount >= _options.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxDispatcherBackgroundService stopped.");
    }
}
