using PeopleFlow.Kernel.Domain;
using PeopleFlow.Security.Redaction;

namespace PeopleFlow.Modules.Tenancy.Core.Entities;

public class Tenant : BaseEntity, IPlatformOwnedEntity
{
    public Tenant() { }
    public Tenant(Guid id) : base(id) { }

    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IndustryPresetCode { get; set; } = "OfficeServices";
    public string DefaultLanguage { get; set; } = "id-ID";
    public string DefaultTimeZoneId { get; set; } = "Asia/Jakarta";
    public bool RequireMfaForAll { get; set; }
    public bool EnforcePayrollSod { get; set; } = true;
    public string Status { get; set; } = "Active";
}

public class SupportAccessGrant : BaseEntity, IPlatformOwnedEntity
{
    public SupportAccessGrant() { }
    public SupportAccessGrant(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public string TicketReference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string AccessLevel { get; set; } = "ReadOnly";
    public bool AllowFinancialRead { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public Guid? AssignedAdminUserId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public class TenancyMembership : BaseEntity
{
    public TenancyMembership() { }
    public TenancyMembership(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsEmergencyAccount { get; set; }
    public DateTimeOffset JoinedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? RevokeReason { get; set; }
}

public class TenancyInvitation : BaseEntity
{
    public TenancyInvitation() { }
    public TenancyInvitation(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public string InvitedEmail { get; set; } = string.Empty;
    public Guid? TargetEmployeeId { get; set; }
    public string InitialRoleAssignmentsJson { get; set; } = "[]";

    [SensitiveData("[REDACTED_TOKEN_HASH]")]
    public string TokenHash { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Guid InvitedByUserId { get; set; }
    public Guid? AcceptedByUserId { get; set; }
}

public class TenancyRole : BaseEntity
{
    public TenancyRole() { }
    public TenancyRole(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemBuiltin { get; set; }
    public bool IsReadOnlyRole { get; set; }
}

/// <summary>
/// Composite-key join entity matching tenancy_role_permissions schema (no id/xmin/audit columns).
/// </summary>
public class TenancyRolePermission
{
    public Guid TenantId { get; set; }
    public Guid RoleId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
}

public class TenancyMemberRoleScope : BaseEntity
{
    public TenancyMemberRoleScope() { }
    public TenancyMemberRoleScope(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid RoleId { get; set; }
    public string ScopeType { get; set; } = "Self";
    public string? ScopeTargetIdsJson { get; set; }
    public DateOnly? DataPeriodFrom { get; set; }
    public DateOnly? DataPeriodTo { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
}

public class ApiIntegrationClient : BaseEntity
{
    public ApiIntegrationClient() { }
    public ApiIntegrationClient(Guid id) : base(id) { }

    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;

    [SensitiveData("[REDACTED_SECRET_HASH]")]
    public string ActiveSecretHash { get; set; } = string.Empty;

    [SensitiveData("[REDACTED_SECRET_HASH]")]
    public string? PreviousSecretHash { get; set; }

    public DateTimeOffset? PreviousSecretExpiresAtUtc { get; set; }
    public string AllowedScopesJson { get; set; } = "[]";
    public string? ScopedLegalEntityIdsJson { get; set; }
    public string? ScopedLocationIdsJson { get; set; }
    public int RateLimitPerMinute { get; set; } = 60;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
}
