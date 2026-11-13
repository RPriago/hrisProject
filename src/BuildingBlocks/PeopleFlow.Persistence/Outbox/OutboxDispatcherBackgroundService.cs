using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;
using PeopleFlow.Security.Redaction;

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
        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        var claimed = await ClaimPendingMessagesAsync(now, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        var processedCount = 0;
        foreach (var entry in claimed)
        {
            if (await ProcessSingleMessageAsync(entry.Id, entry.TenantId, cancellationToken))
            {
                processedCount++;
            }
        }

        return processedCount;
    }

    private async Task<List<(Guid Id, Guid? TenantId)>> ClaimPendingMessagesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var claimScope = _scopeFactory.CreateScope();
        var dbContext = claimScope.ServiceProvider.GetRequiredService<PeopleFlowDbContext>();
        var batchSize = _options.BatchSize;
        var isNpgsql = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL" &&
                       dbContext.Database.GetDbConnection() is Npgsql.NpgsqlConnection;

        if (!isNpgsql)
        {
            var pending = await dbContext.OutboxMessages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.Status == OutboxMessageStatus.Pending)
                .Select(m => new { m.Id, m.TenantId, m.NextAttemptAtUtc })
                .ToListAsync(cancellationToken);

            return pending
                .Where(m => m.NextAttemptAtUtc <= now)
                .OrderBy(m => m.NextAttemptAtUtc)
                .Take(batchSize)
                .Select(m => (m.Id, m.TenantId))
                .ToList();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.outbox_dispatcher_mode', 'true', true);",
            cancellationToken);

        var pendingMessages = await dbContext.OutboxMessages
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

        if (pendingMessages.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return [];
        }

        var leaseUntil = now.AddMinutes(2);
        foreach (var msg in pendingMessages)
        {
            msg.NextAttemptAtUtc = leaseUntil;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return pendingMessages.Select(m => (m.Id, m.TenantId)).ToList();
    }

    private async Task<bool> ProcessSingleMessageAsync(
        Guid messageId,
        Guid? messageTenantId,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PeopleFlowDbContext>();
        var processors = scope.ServiceProvider.GetServices<IOutboxMessageProcessor>().ToList();
        var tenantAccessor = scope.ServiceProvider.GetService<ITenantContextAccessor>();

        var isNpgsql = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL" &&
                       dbContext.Database.GetDbConnection() is Npgsql.NpgsqlConnection;

        using var tenantScope = (messageTenantId.HasValue && tenantAccessor != null)
            ? tenantAccessor.BeginScope(new TenantContext(messageTenantId.Value, "Active"))
            : null;

        await using var transaction = isNpgsql
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        OutboxMessage? message;
        if (isNpgsql)
        {
            if (!messageTenantId.HasValue)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT set_config('app.outbox_dispatcher_mode', 'true', true);",
                    cancellationToken);
            }

            message = await dbContext.OutboxMessages
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken);

            // Reset dispatcher mode BEFORE invoking domain processors so handlers strictly run under tenant RLS
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.outbox_dispatcher_mode', 'false', true);",
                cancellationToken);
        }
        else
        {
            message = await dbContext.OutboxMessages
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        }

        if (message == null || message.Status != OutboxMessageStatus.Pending)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return false;
        }

        try
        {
            message.AttemptCount++;

            foreach (var processor in processors)
            {
                await processor.ProcessAsync(message, cancellationToken);
            }

            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAtUtc = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
            message.LastErrorSummary = null;

            if (isNpgsql && !message.TenantId.HasValue)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT set_config('app.outbox_dispatcher_mode', 'true', true);",
                    cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            // Discard any dirty or cross-tenant entities left in ChangeTracker by the failed processor
            dbContext.ChangeTracker.Clear();

            var errorMsg = SensitiveDataRedactor.SanitizeExceptionSummary(ex, 1000);
            _logger.LogError(
                "Failed to process outbox message {MessageId} of type {MessageType}: {ErrorSummary}",
                messageId,
                message.MessageType,
                errorMsg);

            await using var errTx = isNpgsql
                ? await dbContext.Database.BeginTransactionAsync(CancellationToken.None)
                : null;

            if (isNpgsql && !messageTenantId.HasValue)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT set_config('app.outbox_dispatcher_mode', 'true', true);",
                    CancellationToken.None);
            }

            var tracked = await dbContext.OutboxMessages
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(m => m.Id == messageId, CancellationToken.None);

            if (tracked != null)
            {
                tracked.AttemptCount++;
                tracked.LastErrorSummary = errorMsg;

                if (tracked.AttemptCount >= _options.MaxRetries)
                {
                    tracked.Status = OutboxMessageStatus.DeadLetter;
                    _logger.LogWarning(
                        "Outbox message {MessageId} reached maximum retry limit ({MaxRetries}) and transitioned to DeadLetter",
                        tracked.Id,
                        _options.MaxRetries);
                }
                else
                {
                    tracked.Status = OutboxMessageStatus.Pending;
                    var delaySeconds = _options.BaseBackoffSeconds * Math.Pow(2, tracked.AttemptCount - 1);
                    tracked.NextAttemptAtUtc = (_clock?.UtcNow ?? DateTimeOffset.UtcNow).AddSeconds(delaySeconds);
                }

                await dbContext.SaveChangesAsync(CancellationToken.None);
            }

            if (errTx != null)
            {
                await errTx.CommitAsync(CancellationToken.None);
            }

            return false;
        }
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
