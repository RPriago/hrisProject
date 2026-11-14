using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Modules.Tenancy.Contracts;

public sealed record CreateApiClientRequest(
    string Name,
    IReadOnlyList<string> AllowedScopes,
    IReadOnlyCollection<Guid>? ScopedLegalEntityIds = null,
    IReadOnlyCollection<Guid>? ScopedLocationIds = null,
    int RateLimitPerMinute = 60,
    int ExpiresInDays = 90);

public sealed record ApiClientCreatedDto(
    Guid Id,
    string Name,
    string ClientId,
    string RawSecret,
    DateTimeOffset ExpiresAtUtc,
    int RateLimitPerMinute,
    IReadOnlyList<string> AllowedScopes);

public sealed record ApiClientDto(
    Guid Id,
    string Name,
    string ClientId,
    IReadOnlyList<string> AllowedScopes,
    IReadOnlyList<Guid> ScopedLegalEntityIds,
    IReadOnlyList<Guid> ScopedLocationIds,
    int RateLimitPerMinute,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? PreviousSecretExpiresAtUtc);

public sealed record AuthenticatedApiClientInfo(
    Guid ClientRecordId,
    Guid TenantId,
    string ClientId,
    string Name,
    IReadOnlySet<string> AllowedScopes,
    IReadOnlySet<Guid> ScopedLegalEntityIds,
    IReadOnlySet<Guid> ScopedLocationIds,
    int RateLimitPerMinute);

public interface IApiClientService
{
    Task<IReadOnlyList<ApiClientDto>> ListClientsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Result<ApiClientCreatedDto>> CreateClientAsync(Guid tenantId, Guid actorUserId, CreateApiClientRequest request, CancellationToken cancellationToken = default);
    Task<Result<ApiClientCreatedDto>> RotateSecretAsync(Guid tenantId, Guid actorUserId, Guid clientRecordId, CancellationToken cancellationToken = default);
    Task<Result> RevokeClientAsync(Guid tenantId, Guid actorUserId, Guid clientRecordId, CancellationToken cancellationToken = default);
}

public interface IApiClientAuthenticator
{
    Task<Result<AuthenticatedApiClientInfo>> AuthenticateAsync(string clientId, string rawSecret, CancellationToken cancellationToken = default);
}
