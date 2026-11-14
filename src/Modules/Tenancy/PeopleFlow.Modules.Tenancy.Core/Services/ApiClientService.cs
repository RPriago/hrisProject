using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Security.Authentication;
using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class ApiClientService : IApiClientService
{
    private static readonly IReadOnlySet<string> ValidCatalog =
        new HashSet<string>(Permissions.AllCatalog, StringComparer.Ordinal);

    private readonly TenancyDbContext _db;
    private readonly PasswordHasherService _passwordHasher;
    private readonly ISubscriptionQuotaService _quotaService;
    private readonly IOrganizationUnitValidator _orgValidator;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IClock _clock;

    public ApiClientService(
        TenancyDbContext db,
        PasswordHasherService passwordHasher,
        ISubscriptionQuotaService quotaService,
        IOrganizationUnitValidator orgValidator,
        IPlatformAuditLogWriter auditWriter,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _quotaService = quotaService ?? throw new ArgumentNullException(nameof(quotaService));
        _orgValidator = orgValidator ?? throw new ArgumentNullException(nameof(orgValidator));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<ApiClientDto>> ListClientsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var rows = await _db.ApiIntegrationClients
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new ApiClientDto(
                c.Id,
                c.Name,
                c.ClientId,
                DeserializeStringArray(c.AllowedScopesJson),
                DeserializeGuidArray(c.ScopedLegalEntityIdsJson),
                DeserializeGuidArray(c.ScopedLocationIdsJson),
                c.RateLimitPerMinute,
                c.ExpiresAtUtc,
                c.RevokedAtUtc,
                c.LastUsedAtUtc,
                c.PreviousSecretExpiresAtUtc))
            .ToList();
    }

    public async Task<Result<ApiClientCreatedDto>> CreateClientAsync(
        Guid tenantId,
        Guid actorUserId,
        CreateApiClientRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ApiClientCreatedDto>(new Error("INVALID_NAME", "API client name is required."));
        }

        if (request.AllowedScopes is null || request.AllowedScopes.Count == 0 ||
            request.AllowedScopes.Any(s => !ValidCatalog.Contains(s)))
        {
            return Result.Failure<ApiClientCreatedDto>(new Error("INVALID_SCOPES", "AllowedScopes must contain valid permission codes."));
        }

        var leIds = request.ScopedLegalEntityIds?.ToArray() ?? Array.Empty<Guid>();
        if (leIds.Length > 0 && !await _orgValidator.AllScopeTargetsExistAsync(tenantId, DataScope.LegalEntity, leIds, cancellationToken))
        {
            return Result.Failure<ApiClientCreatedDto>(new Error("INVALID_SCOPE_TARGETS", "Invalid ScopedLegalEntityIds."));
        }

        var locIds = request.ScopedLocationIds?.ToArray() ?? Array.Empty<Guid>();
        if (locIds.Length > 0 && !await _orgValidator.AllScopeTargetsExistAsync(tenantId, DataScope.Branch, locIds, cancellationToken))
        {
            return Result.Failure<ApiClientCreatedDto>(new Error("INVALID_SCOPE_TARGETS", "Invalid ScopedLocationIds."));
        }

        using var _ = _db.BeginOperationTenantScope(tenantId);

        var scopes = request.AllowedScopes.Distinct(StringComparer.Ordinal).ToList();
        var createResult = await TenantTransactionSerializer.ExecuteAsync(_db, tenantId, async () =>
        {
            var now = _clock.UtcNow;
            var existingClients = await _db.ApiIntegrationClients
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);

            var activeCount = existingClients.Count(c => c.ExpiresAtUtc > now);
            var quota = await _quotaService.CheckApiClientQuotaAsync(tenantId, activeCount, cancellationToken);
            if (!quota.IsSuccess)
            {
                return Result.Failure<ApiClientCreatedDto>(quota.Error);
            }

            var clientId = BuildClientId(tenantId);
            var rawSecret = GenerateRawSecret();
            var secretHash = _passwordHasher.HashPassword(rawSecret);
            var expiresAt = now.AddDays(Math.Clamp(request.ExpiresInDays, 1, 365));
            var rateLimit = Math.Clamp(request.RateLimitPerMinute, 1, 6000);

            var entity = new ApiIntegrationClient(Guid.CreateVersion7())
            {
                TenantId = tenantId,
                Name = request.Name.Trim(),
                ClientId = clientId,
                ActiveSecretHash = secretHash,
                AllowedScopesJson = JsonSerializer.Serialize(scopes),
                ScopedLegalEntityIdsJson = leIds.Length > 0 ? JsonSerializer.Serialize(leIds) : null,
                ScopedLocationIdsJson = locIds.Length > 0 ? JsonSerializer.Serialize(locIds) : null,
                RateLimitPerMinute = rateLimit,
                ExpiresAtUtc = expiresAt
            };

            _db.ApiIntegrationClients.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new ApiClientCreatedDto(
                entity.Id,
                entity.Name,
                entity.ClientId,
                rawSecret,
                entity.ExpiresAtUtc,
                entity.RateLimitPerMinute,
                scopes));
        }, cancellationToken);

        if (!createResult.IsSuccess)
        {
            return createResult;
        }

        var dto = createResult.Value;
        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            actorUserId,
            "TenantAdmin",
            "API_CLIENT_CREATED",
            TargetTenantId: tenantId,
            ResourcePath: "/api/v1/settings/integrations",
            MetadataJson: JsonSerializer.Serialize(new { dto.Id, dto.ClientId, scopes })), cancellationToken);

        return createResult;
    }

    public async Task<Result<ApiClientCreatedDto>> RotateSecretAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid clientRecordId,
        CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var entity = await _db.ApiIntegrationClients
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == clientRecordId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<ApiClientCreatedDto>(new Error("CLIENT_NOT_FOUND", "API integration client not found."));
        }

        var now = _clock.UtcNow;
        if (entity.RevokedAtUtc.HasValue || entity.ExpiresAtUtc <= now)
        {
            return Result.Failure<ApiClientCreatedDto>(new Error("CLIENT_INACTIVE", "Cannot rotate secret for a revoked or expired API client."));
        }

        var newRawSecret = GenerateRawSecret();
        entity.PreviousSecretHash = entity.ActiveSecretHash;
        entity.PreviousSecretExpiresAtUtc = now.AddHours(24);
        entity.ActiveSecretHash = _passwordHasher.HashPassword(newRawSecret);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            actorUserId,
            "TenantAdmin",
            "API_CLIENT_SECRET_ROTATED",
            TargetTenantId: tenantId,
            ResourcePath: $"/api/v1/settings/integrations/{clientRecordId}/rotate-secret",
            MetadataJson: JsonSerializer.Serialize(new { entity.Id, entity.ClientId })), cancellationToken);

        return Result.Success(new ApiClientCreatedDto(
            entity.Id,
            entity.Name,
            entity.ClientId,
            newRawSecret,
            entity.ExpiresAtUtc,
            entity.RateLimitPerMinute,
            DeserializeStringArray(entity.AllowedScopesJson)));
    }

    public async Task<Result> RevokeClientAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid clientRecordId,
        CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var entity = await _db.ApiIntegrationClients
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == clientRecordId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure(new Error("CLIENT_NOT_FOUND", "API integration client not found."));
        }

        entity.RevokedAtUtc = _clock.UtcNow;
        entity.PreviousSecretHash = null;
        entity.PreviousSecretExpiresAtUtc = null;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            actorUserId,
            "TenantAdmin",
            "API_CLIENT_REVOKED",
            TargetTenantId: tenantId,
            ResourcePath: $"/api/v1/settings/integrations/{clientRecordId}",
            MetadataJson: JsonSerializer.Serialize(new { entity.Id, entity.ClientId })), cancellationToken);

        return Result.Success();
    }

    internal static bool TryExtractTenantId(string clientId, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        // Format: pfi_<22-char base64url tenantId>_<hex> (base64url itself may contain '_')
        if (clientId.Length < 28 ||
            !clientId.StartsWith("pfi_", StringComparison.Ordinal) ||
            clientId[26] != '_')
        {
            return false;
        }

        var encodedTenant = clientId.Substring(4, 22);
        var base64 = encodedTenant.Replace('-', '+').Replace('_', '/') + "==";
        Span<byte> bytes = stackalloc byte[16];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != 16)
        {
            return false;
        }

        tenantId = new Guid(bytes);
        return true;
    }

    private static string BuildClientId(Guid tenantId)
    {
        var tenantEncoded = Convert.ToBase64String(tenantId.ToByteArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        var randHex = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        return $"pfi_{tenantEncoded}_{randHex}";
    }

    private static string GenerateRawSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    internal static IReadOnlyList<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
    }

    internal static IReadOnlyList<Guid> DeserializeGuidArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Guid>();
        return JsonSerializer.Deserialize<Guid[]>(json) ?? Array.Empty<Guid>();
    }
}
