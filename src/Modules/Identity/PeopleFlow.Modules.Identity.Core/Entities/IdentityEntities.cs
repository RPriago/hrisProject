using PeopleFlow.Kernel.Domain;
using PeopleFlow.Security.Redaction;

namespace PeopleFlow.Modules.Identity.Core.Entities;

public class IdentityUser : BaseEntity, IPlatformOwnedEntity
{
    public IdentityUser() { }
    public IdentityUser(Guid id) : base(id) { }

    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;

    [SensitiveData("[REDACTED_HASH]")]
    public string PasswordHash { get; set; } = string.Empty;

    public bool TwoFactorEnabled { get; set; }

    [SensitiveData("[REDACTED_TOTP_KEY]")]
    public string? AuthenticatorKeyEncrypted { get; set; }

    [SensitiveData("[REDACTED_TOTP_KEY]")]
    public string? PendingAuthenticatorKeyEncrypted { get; set; }

    public DateTimeOffset? PendingAuthenticatorKeyExpiresAtUtc { get; set; }

    public long? LastTotpStepUsed { get; set; }

    [SensitiveData("[REDACTED_RECOVERY_CODES]")]
    public string? RecoveryCodesHashJson { get; set; }

    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public string PreferredLanguage { get; set; } = "id-ID";
    public bool IsPlatformAdmin { get; set; }
    public bool IsBreakGlass { get; set; }
    public bool IsActive { get; set; } = true;
}

public class IdentityUserSession : BaseEntity, IPlatformOwnedEntity
{
    public IdentityUserSession() { }
    public IdentityUserSession(Guid id) : base(id) { }

    public Guid UserId { get; set; }
    public Guid? ActiveTenantId { get; set; }
    public Guid? SupportGrantId { get; set; }
    public DateTimeOffset? MfaVerifiedAtUtc { get; set; }
    public string? IpAddressMasked { get; set; }
    public string? UserAgentSummary { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? RevokeReason { get; set; }
}

public class PasswordResetToken : BaseEntity, IPlatformOwnedEntity
{
    public PasswordResetToken() { }
    public PasswordResetToken(Guid id) : base(id) { }

    public Guid UserId { get; set; }

    [SensitiveData("[REDACTED_TOKEN_HASH]")]
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}
