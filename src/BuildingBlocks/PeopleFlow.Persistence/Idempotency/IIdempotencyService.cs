namespace PeopleFlow.Persistence.Idempotency;

public enum IdempotencyExecutionStatus
{
    Execute,
    CachedResponse,
    ConflictInProgress,
    PayloadMismatch
}

public static class IdempotencyStatus
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string FailedRetriable = "FailedRetriable";
}

public sealed record IdempotencyResolution(
    IdempotencyExecutionStatus Status,
    int? CachedStatusCode = null,
    string? CachedResponseBody = null,
    string? ErrorMessage = null)
{
    public static IdempotencyResolution CanExecute() =>
        new(IdempotencyExecutionStatus.Execute);

    public static IdempotencyResolution Replay(int statusCode, string? responseBody) =>
        new(IdempotencyExecutionStatus.CachedResponse, statusCode, responseBody);

    public static IdempotencyResolution Conflict(string message = "A request with this idempotency key is currently being processed.") =>
        new(IdempotencyExecutionStatus.ConflictInProgress, ErrorMessage: message);

    public static IdempotencyResolution Mismatch(string message = "Idempotency key payload mismatch.") =>
        new(IdempotencyExecutionStatus.PayloadMismatch, ErrorMessage: message);
}

public interface IIdempotencyService
{
    Task<IdempotencyResolution> CheckOrAcquireLockAsync(
        string idempotencyKey,
        string endpointKey,
        string requestPayload,
        Guid? tenantId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        string idempotencyKey,
        string endpointKey,
        int statusCode,
        string? responseBody,
        Guid? tenantId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task MarkFailedRetriableAsync(
        string idempotencyKey,
        string endpointKey,
        Guid? tenantId = null,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    string ComputeRequestHash(string requestBody);
}
