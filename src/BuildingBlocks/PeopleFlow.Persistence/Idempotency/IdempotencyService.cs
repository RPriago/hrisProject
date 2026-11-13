using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Persistence.Idempotency;

public class IdempotencyService : IIdempotencyService
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly ITenantContextAccessor? _tenantContextAccessor;
    private readonly IClock? _clock;

    public IdempotencyService(
        PeopleFlowDbContext dbContext,
        ITenantContextAccessor? tenantContextAccessor = null,
        IClock? clock = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContextAccessor = tenantContextAccessor;
        _clock = clock;
    }

    public async Task<IdempotencyResolution> CheckOrAcquireLockAsync(
        string idempotencyKey,
        string endpointKey,
        string requestPayload,
        Guid? tenantId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKeyAndEndpoint(idempotencyKey, endpointKey);

        if (_dbContext.CurrentTenantId.HasValue && tenantId.HasValue && tenantId.Value != _dbContext.CurrentTenantId.Value)
        {
            throw new InvalidOperationException("Cannot process idempotency record for a different tenant than the active ambient tenant.");
        }

        var effectiveTenantId = ResolveEffectiveTenantId(tenantId);
        var effectiveActorUserId = actorUserId ?? Guid.Empty;

        using var tenantScope = _dbContext.BeginOperationTenantScope(effectiveTenantId);
        var requestHash = ComputeRequestHash(requestPayload);
        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r =>
                r.TenantId == effectiveTenantId &&
                r.ActorUserId == effectiveActorUserId &&
                r.EndpointKey == endpointKey &&
                r.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing != null)
        {
            if (existing.ExpiresAtUtc <= now)
            {
                // Expired: reset and re-execute (optimistic concurrency guards racing workers)
                existing.Status = IdempotencyStatus.InProgress;
                existing.RequestHash = requestHash;
                existing.ResponseStatusCode = null;
                existing.ResponseBodyJson = null;
                existing.ExpiresAtUtc = now.AddHours(24);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return IdempotencyResolution.CanExecute();
                }
                catch (DbUpdateConcurrencyException)
                {
                    _dbContext.Entry(existing).State = EntityState.Detached;
                    return IdempotencyResolution.Conflict();
                }
            }

            if (string.Equals(existing.Status, IdempotencyStatus.Completed, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                {
                    return IdempotencyResolution.Mismatch("Idempotency key payload mismatch.");
                }

                return IdempotencyResolution.Replay(
                    existing.ResponseStatusCode ?? 200,
                    existing.ResponseBodyJson);
            }

            if (string.Equals(existing.Status, IdempotencyStatus.FailedRetriable, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                {
                    return IdempotencyResolution.Mismatch("Idempotency key payload mismatch.");
                }

                existing.Status = IdempotencyStatus.InProgress;
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return IdempotencyResolution.CanExecute();
                }
                catch (DbUpdateConcurrencyException)
                {
                    _dbContext.Entry(existing).State = EntityState.Detached;
                    return IdempotencyResolution.Conflict();
                }
            }

            // InProgress
            return IdempotencyResolution.Conflict();
        }

        // Record does not exist: attempt to create InProgress lock
        var newRecord = new IdempotencyRecord
        {
            TenantId = effectiveTenantId,
            ActorUserId = effectiveActorUserId,
            EndpointKey = endpointKey,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Status = IdempotencyStatus.InProgress,
            ExpiresAtUtc = now.AddHours(24)
        };

        _dbContext.IdempotencyRecords.Add(newRecord);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return IdempotencyResolution.CanExecute();
        }
        catch (DbUpdateException)
        {
            // Simultaneous concurrency conflict: another request inserted the same key
            _dbContext.Entry(newRecord).State = EntityState.Detached;

            var collision = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.TenantId == effectiveTenantId &&
                    r.ActorUserId == effectiveActorUserId &&
                    r.EndpointKey == endpointKey &&
                    r.IdempotencyKey == idempotencyKey, cancellationToken);

            if (collision != null)
            {
                if (string.Equals(collision.Status, IdempotencyStatus.Completed, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(collision.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return IdempotencyResolution.Mismatch("Idempotency key payload mismatch.");
                    }

                    return IdempotencyResolution.Replay(
                        collision.ResponseStatusCode ?? 200,
                        collision.ResponseBodyJson);
                }
            }

            return IdempotencyResolution.Conflict();
        }
    }

    public async Task CompleteAsync(
        string idempotencyKey,
        string endpointKey,
        int statusCode,
        string? responseBody,
        Guid? tenantId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKeyAndEndpoint(idempotencyKey, endpointKey);

        if (_dbContext.CurrentTenantId.HasValue && tenantId.HasValue && tenantId.Value != _dbContext.CurrentTenantId.Value)
        {
            throw new InvalidOperationException("Cannot process idempotency record for a different tenant than the active ambient tenant.");
        }

        var effectiveTenantId = ResolveEffectiveTenantId(tenantId);
        var effectiveActorUserId = actorUserId ?? Guid.Empty;

        using var tenantScope = _dbContext.BeginOperationTenantScope(effectiveTenantId);

        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r =>
                r.TenantId == effectiveTenantId &&
                r.ActorUserId == effectiveActorUserId &&
                r.EndpointKey == endpointKey &&
                r.IdempotencyKey == idempotencyKey, cancellationToken);

        if (record != null)
        {
            record.Status = IdempotencyStatus.Completed;
            record.ResponseStatusCode = statusCode;
            record.ResponseBodyJson = responseBody;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkFailedRetriableAsync(
        string idempotencyKey,
        string endpointKey,
        Guid? tenantId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKeyAndEndpoint(idempotencyKey, endpointKey);

        if (_dbContext.CurrentTenantId.HasValue && tenantId.HasValue && tenantId.Value != _dbContext.CurrentTenantId.Value)
        {
            throw new InvalidOperationException("Cannot process idempotency record for a different tenant than the active ambient tenant.");
        }

        // Clear any dirty entities tracked from failed action execution
        _dbContext.ChangeTracker.Clear();

        var effectiveTenantId = ResolveEffectiveTenantId(tenantId);
        var effectiveActorUserId = actorUserId ?? Guid.Empty;

        using var tenantScope = _dbContext.BeginOperationTenantScope(effectiveTenantId);

        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r =>
                r.TenantId == effectiveTenantId &&
                r.ActorUserId == effectiveActorUserId &&
                r.EndpointKey == endpointKey &&
                r.IdempotencyKey == idempotencyKey, cancellationToken);

        if (record != null)
        {
            record.Status = IdempotencyStatus.FailedRetriable;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private Guid ResolveEffectiveTenantId(Guid? tenantId)
    {
        var effectiveTenantId = tenantId ?? _tenantContextAccessor?.TenantId ?? _dbContext.CurrentTenantId;
        if (!effectiveTenantId.HasValue || effectiveTenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Active tenant context is required for idempotency operations.");
        }

        return effectiveTenantId.Value;
    }

    public string ComputeRequestHash(string requestBody)
    {
        var canonical = NormalizeRequestBody(requestBody);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeRequestBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
            doc.WriteTo(writer);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return body.Trim();
        }
    }

    private static void ValidateKeyAndEndpoint(string idempotencyKey, string endpointKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));
        }

        if (idempotencyKey.Length > 128)
        {
            throw new ArgumentException("Idempotency key must not exceed 128 characters.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(endpointKey))
        {
            throw new ArgumentException("Endpoint key cannot be empty.", nameof(endpointKey));
        }

        if (endpointKey.Length > 160)
        {
            throw new ArgumentException("Endpoint key must not exceed 160 characters.", nameof(endpointKey));
        }
    }
}
