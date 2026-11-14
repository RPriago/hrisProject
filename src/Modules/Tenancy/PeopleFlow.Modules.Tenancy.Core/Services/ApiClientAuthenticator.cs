using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Security.Authentication;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class ApiClientAuthenticator : IApiClientAuthenticator
{
    private readonly TenancyDbContext _db;
    private readonly PasswordHasherService _passwordHasher;
    private readonly ApiClientRateLimiter _rateLimiter;
    private readonly IClock _clock;
    private readonly ITenantSubscriptionQueryService? _subscriptionQuery;

    public ApiClientAuthenticator(
        TenancyDbContext db,
        PasswordHasherService passwordHasher,
        ApiClientRateLimiter rateLimiter,
        IClock clock,
        ITenantSubscriptionQueryService? subscriptionQuery = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _subscriptionQuery = subscriptionQuery;
    }

    public async Task<Result<AuthenticatedApiClientInfo>> AuthenticateAsync(
        string clientId,
        string rawSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(rawSecret))
        {
            return Result.Failure<AuthenticatedApiClientInfo>(Error.Unauthorized("API_CLIENT_INVALID", "Client credentials are invalid."));
        }

        if (!ApiClientService.TryExtractTenantId(clientId, out var parsedTenantId))
        {
            return Result.Failure<AuthenticatedApiClientInfo>(Error.Unauthorized("API_CLIENT_INVALID", "Client credentials are invalid."));
        }

        using var _ = _db.BeginOperationTenantScope(parsedTenantId);

        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == parsedTenantId, cancellationToken);
        if (tenant is null || !string.Equals(tenant.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<AuthenticatedApiClientInfo>(Error.Forbidden("API_CLIENT_TENANT_INACTIVE", "Tenant is not active."));
        }

        if (_subscriptionQuery is not null)
        {
            var snap = await _subscriptionQuery.GetSnapshotAsync(parsedTenantId, cancellationToken);
            var isSubscriptionAllowed = snap is not null && (
                string.Equals(snap.Status, SubscriptionStatusConstants.Trial, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(snap.Status, SubscriptionStatusConstants.Active, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(snap.Status, SubscriptionStatusConstants.PastDue, StringComparison.OrdinalIgnoreCase));
            if (!isSubscriptionAllowed)
            {
                return Result.Failure<AuthenticatedApiClientInfo>(Error.Forbidden("API_CLIENT_SUBSCRIPTION_INACTIVE", "Tenant subscription is missing or inactive."));
            }
        }

        var client = await _db.ApiIntegrationClients
            .FirstOrDefaultAsync(c => c.TenantId == parsedTenantId && c.ClientId == clientId, cancellationToken);

        var now = _clock.UtcNow;
        if (client is null || client.RevokedAtUtc.HasValue || client.ExpiresAtUtc <= now)
        {
            return Result.Failure<AuthenticatedApiClientInfo>(Error.Unauthorized("INVALID_API_CLIENT", "Client credentials are invalid, expired, or revoked."));
        }

        if (!_rateLimiter.TryConsume(client.Id, client.RateLimitPerMinute))
        {
            return Result.Failure<AuthenticatedApiClientInfo>(new Error("API_CLIENT_RATE_LIMITED", "API client per-minute rate limit exceeded."));
        }

        var activeValid = _passwordHasher.VerifyPassword(client.ActiveSecretHash, rawSecret);
        var previousValid = !activeValid
            && client.PreviousSecretHash is not null
            && client.PreviousSecretExpiresAtUtc.HasValue
            && client.PreviousSecretExpiresAtUtc.Value > now
            && _passwordHasher.VerifyPassword(client.PreviousSecretHash, rawSecret);

        if (!activeValid && !previousValid)
        {
            return Result.Failure<AuthenticatedApiClientInfo>(Error.Unauthorized("INVALID_API_CLIENT", "Client credentials are invalid."));
        }

        if (!client.LastUsedAtUtc.HasValue || (now - client.LastUsedAtUtc.Value) > TimeSpan.FromMinutes(1))
        {
            client.LastUsedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new AuthenticatedApiClientInfo(
            client.Id,
            client.TenantId,
            client.ClientId,
            client.Name,
            new HashSet<string>(ApiClientService.DeserializeStringArray(client.AllowedScopesJson), StringComparer.Ordinal),
            new HashSet<Guid>(ApiClientService.DeserializeGuidArray(client.ScopedLegalEntityIdsJson)),
            new HashSet<Guid>(ApiClientService.DeserializeGuidArray(client.ScopedLocationIdsJson)),
            client.RateLimitPerMinute));
    }
}
