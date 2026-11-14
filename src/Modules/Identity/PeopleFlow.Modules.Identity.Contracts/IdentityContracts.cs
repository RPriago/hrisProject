using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Modules.Identity.Contracts;

public sealed record AuthenticatedUserProfile(
    Guid UserId,
    string Email,
    string PreferredLanguage,
    bool TwoFactorEnabled,
    bool IsPlatformAdmin,
    bool IsBreakGlass,
    bool IsActive);

public enum LoginOutcomeStatus
{
    Authenticated = 1,
    MfaChallengeRequired = 2,
    MfaSetupRequired = 3,
    TenantSelectionRequired = 4
}

public sealed record LoginResultDto(
    LoginOutcomeStatus Status,
    Guid UserId,
    Guid SessionId,
    Guid? ActiveTenantId,
    bool IsPlatformAdmin,
    bool IsBreakGlass,
    bool MfaVerified);

public sealed record MfaSetupDto(
    string Base32Secret,
    string OtpAuthUri);

public sealed record MfaEnableResultDto(
    IReadOnlyList<string> RecoveryCodes,
    LoginResultDto Session);

public sealed record UserSessionDto(
    Guid Id,
    Guid? ActiveTenantId,
    Guid? SupportGrantId,
    DateTimeOffset? MfaVerifiedAtUtc,
    string? IpAddressMasked,
    string? UserAgentSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsCurrent);

public sealed record ResolvedSessionInfo(
    Guid SessionId,
    Guid UserId,
    string Email,
    bool UserIsActive,
    bool IsPlatformAdmin,
    bool IsBreakGlass,
    bool TwoFactorEnabled,
    Guid? ActiveTenantId,
    Guid? SupportGrantId,
    bool IsMfaVerified,
    DateTimeOffset ExpiresAtUtc);

public interface IIdentityAuthService
{
    Task<Result<LoginResultDto>> LoginAsync(
        string email,
        string password,
        string? incidentReason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<Result<LoginResultDto>> VerifyMfaAsync(
        Guid userId,
        Guid sessionId,
        string codeOrRecoveryCode,
        CancellationToken cancellationToken = default);

    Task<Result<MfaSetupDto>> SetupMfaAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<Result<MfaEnableResultDto>> EnableMfaAsync(Guid userId, Guid sessionId, string totpCode, CancellationToken cancellationToken = default);

    Task<Result<MfaSetupDto>> SetupMfaRotationAsync(
        Guid userId,
        Guid sessionId,
        string currentPassword,
        string currentCode,
        CancellationToken cancellationToken = default);

    Task<Result<MfaEnableResultDto>> ConfirmMfaRotationAsync(
        Guid userId,
        Guid sessionId,
        string newCode,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<string>>> RegenerateRecoveryCodesAsync(
        Guid userId,
        Guid sessionId,
        string currentPassword,
        string currentCode,
        CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(Guid sessionId, string reason = "UserLogout", CancellationToken cancellationToken = default);

    Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSessionDto>> ListActiveSessionsAsync(Guid userId, Guid currentSessionId, CancellationToken cancellationToken = default);

    Task<Result> RevokeSessionAsync(Guid userId, Guid targetSessionId, string reason = "UserRevokedSession", CancellationToken cancellationToken = default);

    Task<AuthenticatedUserProfile?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface ISessionContextResolver
{
    Task<ResolvedSessionInfo?> ResolveActiveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<Result> UpdateActiveTenantAsync(Guid sessionId, Guid? activeTenantId, CancellationToken cancellationToken = default);
    Task<Result> SetSupportGrantAsync(Guid sessionId, Guid? supportGrantId, Guid? targetTenantId, CancellationToken cancellationToken = default);
    Task<int> ClearSupportGrantSessionsAsync(Guid supportGrantId, CancellationToken cancellationToken = default);
}

public interface IIdentityProvisioningService
{
    Task<AuthenticatedUserProfile?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Result<AuthenticatedUserProfile>> FindOrCreateUserAsync(
        string email,
        string? initialPassword,
        string preferredLanguage = "id-ID",
        bool isPlatformAdmin = false,
        bool isBreakGlass = false,
        CancellationToken cancellationToken = default);
}
