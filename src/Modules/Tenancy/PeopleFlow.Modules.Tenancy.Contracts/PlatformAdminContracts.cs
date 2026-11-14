using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Modules.Tenancy.Contracts;

public sealed record ProvisionTenantRequest(
    string Slug,
    string DisplayName,
    string IndustryPresetCode,
    string InitialPlanCode,
    string OwnerEmail,
    string? OwnerInitialPassword = null,
    bool StartAsTrial = true,
    string DefaultLanguage = "id-ID",
    string DefaultTimeZoneId = "Asia/Jakarta");

public sealed record ProvisionedTenantDto(
    Guid TenantId,
    string Slug,
    string DisplayName,
    string PlanCode,
    string SubscriptionStatus,
    Guid OwnerUserId);

public sealed record PlatformTenantSummaryDto(
    Guid TenantId,
    string Slug,
    string DisplayName,
    string IndustryPresetCode,
    string Status,
    string? PlanCode,
    string? SubscriptionStatus,
    bool RequireMfaForAll,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateSupportGrantRequest(
    string TicketReference,
    string Reason,
    string AccessLevel = "ReadOnly",
    bool AllowFinancialRead = false,
    Guid? AssignedAdminUserId = null,
    int DurationMinutes = 60);

public sealed record SupportAccessGrantDto(
    Guid Id,
    Guid TenantId,
    string TicketReference,
    string Reason,
    string AccessLevel,
    bool AllowFinancialRead,
    Guid ApprovedByUserId,
    Guid? AssignedAdminUserId,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc);

public sealed record EnterSupportSessionRequest(Guid GrantId);

public sealed record SupportSessionEnteredDto(
    Guid GrantId,
    Guid TenantId,
    string AccessLevel,
    bool AllowFinancialRead,
    DateTimeOffset ExpiresAtUtc);

public sealed record PlatformAuditLogDto(
    Guid Id,
    Guid? ActorUserId,
    string ActorRole,
    string Action,
    Guid? TargetTenantId,
    Guid? SupportGrantId,
    string ResourcePath,
    Guid CorrelationId,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

public interface IPlatformTenantService
{
    Task<IReadOnlyList<PlatformTenantSummaryDto>> ListTenantsAsync(CancellationToken cancellationToken = default);
    Task<Result<ProvisionedTenantDto>> ProvisionTenantAsync(Guid platformAdminUserId, ProvisionTenantRequest request, CancellationToken cancellationToken = default);
    Task<Result> SuspendTenantAsync(Guid platformAdminUserId, Guid tenantId, string reason, CancellationToken cancellationToken = default);
    Task<Result> ReactivateTenantAsync(Guid platformAdminUserId, Guid tenantId, CancellationToken cancellationToken = default);
}

public interface ISupportAccessGrantService
{
    Task<IReadOnlyList<SupportAccessGrantDto>> ListGrantsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Result<SupportAccessGrantDto>> CreateGrantAsync(Guid tenantId, Guid ownerUserId, CreateSupportGrantRequest request, CancellationToken cancellationToken = default);
    Task<Result> RevokeGrantAsync(Guid tenantId, Guid actorUserId, Guid grantId, CancellationToken cancellationToken = default);
    Task<Result<SupportSessionEnteredDto>> EnterSupportSessionAsync(Guid adminUserId, Guid sessionId, Guid grantId, CancellationToken cancellationToken = default);
    Task<Result> ExitSupportSessionAsync(Guid adminUserId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAuditLogDto>> ListTenantAuditLogsAsync(Guid tenantId, int take = 100, CancellationToken cancellationToken = default);
}
