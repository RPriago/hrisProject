using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Identity.Core.Entities;
using PeopleFlow.Modules.Identity.Core.Persistence;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Outbox;
using PeopleFlow.Security.Authentication;
using PeopleFlow.Security.Redaction;

namespace PeopleFlow.Modules.Identity.Core.Services;

public sealed class AuthService : IIdentityAuthService, IIdentityProvisioningService
{
    private const int MinPasswordLength = 12;
    private const int MaxFailedAttempts = 5;
    private const int MaxEmailLength = 256;
    private const int MaxIncidentReasonLength = 500;
    private const int MaxMfaCodeLength = 32;
    private const int MaxResetTokenLength = 256;
    private const int MaxPreferredLanguageLength = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PendingMfaSetupLifetime = TimeSpan.FromMinutes(15);

    private readonly IdentityDbContext _dbContext;
    private readonly PasswordHasherService _passwordHasher;
    private readonly TotpService _totpService;
    private readonly IClock _clock;
    private readonly IPlatformAuditLogWriter _platformAuditLogWriter;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly ITenantMembershipReader? _tenantMembershipReader;
    private readonly string _dummyHash;

    public AuthService(
        IdentityDbContext dbContext,
        PasswordHasherService passwordHasher,
        TotpService totpService,
        IClock clock,
        IPlatformAuditLogWriter platformAuditLogWriter,
        IOutboxPublisher outboxPublisher,
        ITenantMembershipReader? tenantMembershipReader = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _totpService = totpService ?? throw new ArgumentNullException(nameof(totpService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _platformAuditLogWriter = platformAuditLogWriter ?? throw new ArgumentNullException(nameof(platformAuditLogWriter));
        _outboxPublisher = outboxPublisher ?? throw new ArgumentNullException(nameof(outboxPublisher));
        _tenantMembershipReader = tenantMembershipReader;
        _dummyHash = _passwordHasher.HashPassword("DummyTimingEqualization!#2026");
    }

    public async Task<Result<LoginResultDto>> LoginAsync(
        string email,
        string password,
        string? incidentReason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Result.Failure<LoginResultDto>(Error.Unauthorized("INVALID_CREDENTIALS", "Invalid email or password."));
        }

        if (email.Trim().Length > MaxEmailLength || password.Length > PasswordHasherService.MaxPasswordLength)
        {
            _passwordHasher.VerifyPassword(_dummyHash, "timing-equalizer-bounded");
            return Result.Failure<LoginResultDto>(Error.Unauthorized("INVALID_CREDENTIALS", "Invalid email or password."));
        }

        var normalized = email.Trim().ToUpperInvariant();
        var user = await _dbContext.IdentityUsers
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);

        var now = _clock.UtcNow;
        if (user == null || !user.IsActive)
        {
            _passwordHasher.VerifyPassword(_dummyHash, password);
            return Result.Failure<LoginResultDto>(Error.Unauthorized("INVALID_CREDENTIALS", "Invalid email or password."));
        }

        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now)
        {
            _passwordHasher.VerifyPassword(_dummyHash, password);
            return Result.Failure<LoginResultDto>(Error.Unauthorized("INVALID_CREDENTIALS", "Invalid email or password."));
        }

        if (!_passwordHasher.VerifyPassword(user.PasswordHash, password))
        {
            await IncrementFailedAttemptsAtomicAsync(user, now, cancellationToken);
            return Result.Failure<LoginResultDto>(Error.Unauthorized("INVALID_CREDENTIALS", "Invalid email or password."));
        }

        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;

        if (user.IsBreakGlass)
        {
            if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.AuthenticatorKeyEncrypted))
            {
                return Result.Failure<LoginResultDto>(Error.Forbidden("BREAK_GLASS_MFA_REQUIRED", "Break-glass accounts require active MFA configuration."));
            }

            if (string.IsNullOrWhiteSpace(incidentReason) || incidentReason.Trim().Length > MaxIncidentReasonLength)
            {
                return Result.Failure<LoginResultDto>(Error.Validation("BREAK_GLASS_REASON_REQUIRED", $"Incident reason (1..{MaxIncidentReasonLength} characters) is mandatory for break-glass authentication."));
            }
        }

        Guid? singleActiveTenantId = null;
        var tenantRequiresMfa = false;
        var outcome = LoginOutcomeStatus.Authenticated;

        if (_tenantMembershipReader != null)
        {
            var memberships = await _tenantMembershipReader.GetActiveMembershipsForUserAsync(user.Id, cancellationToken);
            if (memberships.Count == 1)
            {
                singleActiveTenantId = memberships[0].TenantId;
                tenantRequiresMfa = memberships[0].RequireMfaForAll;
            }
            else if (memberships.Count > 1 && !user.IsPlatformAdmin)
            {
                outcome = LoginOutcomeStatus.TenantSelectionRequired;
            }
        }

        var mfaRequired = user.TwoFactorEnabled || user.IsPlatformAdmin || user.IsBreakGlass || tenantRequiresMfa;
        if (mfaRequired)
        {
            outcome = user.TwoFactorEnabled
                ? LoginOutcomeStatus.MfaChallengeRequired
                : LoginOutcomeStatus.MfaSetupRequired;
        }

        var sessionDuration = user.IsBreakGlass ? TimeSpan.FromMinutes(60) : TimeSpan.FromHours(12);
        var session = new IdentityUserSession(Guid.CreateVersion7())
        {
            UserId = user.Id,
            ActiveTenantId = singleActiveTenantId,
            MfaVerifiedAtUtc = null,
            IpAddressMasked = MaskIpAddress(ipAddress),
            UserAgentSummary = Truncate(userAgent, 250),
            LastSeenAtUtc = now,
            ExpiresAtUtc = now.Add(sessionDuration)
        };

        if (user.IsBreakGlass)
        {
            var safeReason = SensitiveDataRedactor.RedactSecretsFromText(incidentReason!.Trim());
            var payloadJson = JsonSerializer.Serialize(new { userId = user.Id, tenantId = singleActiveTenantId, reason = safeReason, occurredAtUtc = now });
            var correlationId = Guid.CreateVersion7();

            await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            _dbContext.IdentityUserSessions.Add(session);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (_dbContext.Database.IsNpgsql() && _outboxPublisher is OutboxPublisher)
            {
                if (singleActiveTenantId.HasValue)
                {
                    var tidText = singleActiveTenantId.Value.ToString();
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tidText}, true);",
                        cancellationToken);
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO infra_outbox_messages (id, tenant_id, message_type, payload_json, correlation_id, status, attempt_count, next_attempt_at_utc, created_at_utc) VALUES ({Guid.CreateVersion7()}, {singleActiveTenantId.Value}, {PlatformOutboxMessageTypes.BreakGlassLoginAlert}, CAST({payloadJson} AS jsonb), {correlationId}, {OutboxMessageStatus.Pending}, 0, {now}, {now});",
                        cancellationToken);
                }
                else
                {
                    Guid? createdBy = user.Id;
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT fn_enqueue_platform_outbox_message({Guid.CreateVersion7()}, {PlatformOutboxMessageTypes.BreakGlassLoginAlert}, CAST({payloadJson} AS jsonb), {correlationId}, {now}, {now}, CAST({createdBy} AS uuid));",
                        cancellationToken);
                }
            }
            else if (singleActiveTenantId.HasValue)
            {
                await _outboxPublisher.PublishRawAsync(
                    messageType: PlatformOutboxMessageTypes.BreakGlassLoginAlert,
                    payloadJson: payloadJson,
                    correlationId: correlationId,
                    tenantId: singleActiveTenantId,
                    autoSave: true,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _outboxPublisher.PublishPlatformAsync(
                    messageType: PlatformOutboxMessageTypes.BreakGlassLoginAlert,
                    payloadJson: payloadJson,
                    correlationId: correlationId,
                    cancellationToken: cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

            await _platformAuditLogWriter.WriteAsync(new PlatformAuditEntry(
                ActorUserId: user.Id,
                ActorRole: "BreakGlass",
                Action: "BreakGlassLogin",
                TargetTenantId: singleActiveTenantId,
                ResourcePath: "/api/v1/auth/login",
                Reason: incidentReason!.Trim()), cancellationToken);
        }
        else
        {
            _dbContext.IdentityUserSessions.Add(session);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new LoginResultDto(
            outcome,
            user.Id,
            session.Id,
            session.ActiveTenantId,
            user.IsPlatformAdmin,
            user.IsBreakGlass,
            MfaVerified: !mfaRequired));
    }

    public async Task<Result<LoginResultDto>> VerifyMfaAsync(
        Guid userId,
        Guid sessionId,
        string codeOrRecoveryCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeOrRecoveryCode))
        {
            return Result.Failure<LoginResultDto>(Error.Validation("MFA_CODE_REQUIRED", "TOTP or recovery code is required."));
        }

        if (codeOrRecoveryCode.Trim().Length > MaxMfaCodeLength)
        {
            return Result.Failure<LoginResultDto>(Error.Unauthorized("MFA_CODE_INVALID", "Invalid TOTP or recovery code."));
        }

        var now = _clock.UtcNow;
        var (user, session, guardError) = await LoadMfaEligibleUserAndSessionAsync(
            userId,
            sessionId,
            requireMfaVerifiedSession: false,
            now,
            cancellationToken);
        if (guardError != null)
        {
            return Result.Failure<LoginResultDto>(guardError);
        }

        if (!user!.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.AuthenticatorKeyEncrypted))
        {
            return Result.Failure<LoginResultDto>(Error.Validation("MFA_NOT_CONFIGURED", "MFA is not enabled on this account."));
        }

        var secret = _totpService.UnprotectSecret(user.AuthenticatorKeyEncrypted);
        var verified = VerifyTotpWithReplayProtection(user, secret, codeOrRecoveryCode, now, enforceLastStep: true, out var matchedStep);
        if (verified)
        {
            user.LastTotpStepUsed = matchedStep;
            user.AccessFailedCount = 0;
            user.LockoutEndUtc = null;
            session!.MfaVerifiedAtUtc = now;
            session.LastSeenAtUtc = now;

            if (!await TrySaveChangesAsync(cancellationToken))
            {
                return Result.Failure<LoginResultDto>(Error.Conflict("MFA_STATE_CONFLICT", "MFA state changed concurrently. Please try again."));
            }

            return Result.Success(BuildVerifiedLoginResult(user, session));
        }

        if (!string.IsNullOrWhiteSpace(user.RecoveryCodesHashJson))
        {
            var originalJson = user.RecoveryCodesHashJson;
            List<string> hashes;
            try
            {
                hashes = JsonSerializer.Deserialize<List<string>>(originalJson) ?? new List<string>();
            }
            catch (JsonException)
            {
                hashes = new List<string>();
            }

            var candidateHash = HashSha256Hex(codeOrRecoveryCode.Trim().ToUpperInvariant());
            var matchIdx = hashes.FindIndex(h => CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(h),
                Encoding.UTF8.GetBytes(candidateHash)));
            if (matchIdx >= 0)
            {
                hashes.RemoveAt(matchIdx);
                var updatedJson = JsonSerializer.Serialize(hashes);
                await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                var claimed = await _dbContext.IdentityUsers
                    .Where(u => u.Id == user.Id && u.RecoveryCodesHashJson == originalJson)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.RecoveryCodesHashJson, updatedJson)
                        .SetProperty(u => u.AccessFailedCount, 0)
                        .SetProperty(u => u.LockoutEndUtc, (DateTimeOffset?)null), cancellationToken);
                if (claimed != 1)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Result.Failure<LoginResultDto>(Error.Conflict("MFA_STATE_CONFLICT", "MFA state changed concurrently. Please try again."));
                }

                var sessionClaimed = await _dbContext.IdentityUserSessions
                    .Where(s => s.Id == session!.Id && s.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.MfaVerifiedAtUtc, now)
                        .SetProperty(x => x.LastSeenAtUtc, now), cancellationToken);
                if (sessionClaimed != 1)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Result.Failure<LoginResultDto>(Error.Unauthorized("SESSION_INVALID", "Session is missing, expired, or revoked."));
                }

                await tx.CommitAsync(cancellationToken);
                session!.MfaVerifiedAtUtc = now;
                session.LastSeenAtUtc = now;
                return Result.Success(BuildVerifiedLoginResult(user, session));
            }
        }

        var lockedOut = await RecordFailedMfaAttemptAsync(user, session!, now, "/api/v1/auth/mfa/verify", cancellationToken);
        return Result.Failure<LoginResultDto>(lockedOut
            ? Error.Unauthorized("MFA_LOCKED_OUT", "Too many failed verification attempts. Try again later.")
            : Error.Unauthorized("MFA_CODE_INVALID", "Invalid TOTP or recovery code."));
    }

    public async Task<Result<MfaSetupDto>> SetupMfaAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var (user, session, guardError) = await LoadMfaEligibleUserAndSessionAsync(
            userId,
            sessionId,
            requireMfaVerifiedSession: false,
            now,
            cancellationToken);
        if (guardError != null)
        {
            return Result.Failure<MfaSetupDto>(guardError);
        }

        if (user!.TwoFactorEnabled)
        {
            await WriteMfaAuditAsync(user, session!, "MfaSetupRejectedAlreadyEnabled", "/api/v1/me/mfa/setup", "MFA already enabled", cancellationToken);
            return Result.Failure<MfaSetupDto>(Error.Conflict("MFA_ALREADY_ENABLED", "MFA is already enabled. Use authenticated MFA rotation instead."));
        }

        var secret = _totpService.GenerateBase32Secret();
        user.PendingAuthenticatorKeyEncrypted = _totpService.ProtectSecret(secret);
        user.PendingAuthenticatorKeyExpiresAtUtc = now.Add(PendingMfaSetupLifetime);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<MfaSetupDto>(Error.Conflict("MFA_STATE_CONFLICT", "MFA state changed concurrently. Please try again."));
        }

        return Result.Success(new MfaSetupDto(secret, _totpService.BuildOtpAuthUri("PeopleFlow", user.Email, secret)));
    }

    public async Task<Result<MfaEnableResultDto>> EnableMfaAsync(
        Guid userId,
        Guid sessionId,
        string totpCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(totpCode))
        {
            return Result.Failure<MfaEnableResultDto>(Error.Validation("MFA_CODE_REQUIRED", "Verification code is required."));
        }

        if (totpCode.Trim().Length > MaxMfaCodeLength)
        {
            return Result.Failure<MfaEnableResultDto>(Error.Unauthorized("MFA_CODE_INVALID", "Invalid verification code."));
        }

        var now = _clock.UtcNow;
        var (user, session, guardError) = await LoadMfaEligibleUserAndSessionAsync(
            userId,
            sessionId,
            requireMfaVerifiedSession: false,
            now,
            cancellationToken);
        if (guardError != null)
        {
            return Result.Failure<MfaEnableResultDto>(guardError);
        }

        if (user!.TwoFactorEnabled)
        {
            await WriteMfaAuditAsync(user, session!, "MfaEnableRejectedAlreadyEnabled", "/api/v1/me/mfa/enable", "MFA already enabled", cancellationToken);
            return Result.Failure<MfaEnableResultDto>(Error.Conflict("MFA_ALREADY_ENABLED", "MFA is already enabled on this account."));
        }

        if (string.IsNullOrWhiteSpace(user.PendingAuthenticatorKeyEncrypted) ||
            !user.PendingAuthenticatorKeyExpiresAtUtc.HasValue ||
            user.PendingAuthenticatorKeyExpiresAtUtc.Value <= now)
        {
            if (user.PendingAuthenticatorKeyEncrypted != null || user.PendingAuthenticatorKeyExpiresAtUtc.HasValue)
            {
                user.PendingAuthenticatorKeyEncrypted = null;
                user.PendingAuthenticatorKeyExpiresAtUtc = null;
                await TrySaveChangesAsync(cancellationToken);
            }

            return Result.Failure<MfaEnableResultDto>(Error.Validation("MFA_SETUP_REQUIRED", "Run MFA setup or restart expired setup before enabling MFA."));
        }

        var secret = _totpService.UnprotectSecret(user.PendingAuthenticatorKeyEncrypted);
        if (!VerifyTotpWithReplayProtection(user, secret, totpCode, now, enforceLastStep: true, out var matchedStep))
        {
            var lockedOut = await RecordFailedMfaAttemptAsync(user, session!, now, "/api/v1/me/mfa/enable", cancellationToken);
            return Result.Failure<MfaEnableResultDto>(lockedOut
                ? Error.Unauthorized("MFA_LOCKED_OUT", "Too many failed verification attempts. Try again later.")
                : Error.Unauthorized("MFA_CODE_INVALID", "Invalid verification code."));
        }

        var plaintextCodes = GenerateRecoveryCodes(8);
        var hashedCodes = plaintextCodes.Select(c => HashSha256Hex(c.ToUpperInvariant())).ToList();

        user.AuthenticatorKeyEncrypted = user.PendingAuthenticatorKeyEncrypted;
        user.PendingAuthenticatorKeyEncrypted = null;
        user.PendingAuthenticatorKeyExpiresAtUtc = null;
        user.LastTotpStepUsed = matchedStep;
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.TwoFactorEnabled = true;
        user.RecoveryCodesHashJson = JsonSerializer.Serialize(hashedCodes);
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        session!.MfaVerifiedAtUtc = now;
        session.LastSeenAtUtc = now;
        await RevokeOtherActiveSessionsAsync(user.Id, session.Id, now, "MfaEnabled", cancellationToken);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<MfaEnableResultDto>(Error.Conflict("MFA_STATE_CONFLICT", "MFA state changed concurrently. Please try again."));
        }

        await WriteMfaAuditAsync(user, session, "MfaEnabled", "/api/v1/me/mfa/enable", "Initial MFA enrollment completed", cancellationToken);
        return Result.Success(new MfaEnableResultDto(plaintextCodes, BuildVerifiedLoginResult(user, session)));
    }

    public async Task<Result<MfaSetupDto>> SetupMfaRotationAsync(
        Guid userId,
        Guid sessionId,
        string currentPassword,
        string currentCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(currentCode))
        {
            return Result.Failure<MfaSetupDto>(Error.Validation("MFA_STEP_UP_REQUIRED", "Current password and verification code are required."));
        }

        if (currentPassword.Length > PasswordHasherService.MaxPasswordLength || currentCode.Trim().Length > MaxMfaCodeLength)
        {
            return Result.Failure<MfaSetupDto>(Error.Unauthorized("MFA_STEP_UP_INVALID", "Current password and valid verification code are required."));
        }

        var now = _clock.UtcNow;
        var (user, session, guardError) = await LoadMfaEligibleUserAndSessionAsync(
            userId,
            sessionId,
            requireMfaVerifiedSession: true,
            now,
            cancellationToken);
        if (guardError != null)
        {
            return Result.Failure<MfaSetupDto>(guardError);
        }

        if (!user!.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.AuthenticatorKeyEncrypted))
        {
            return Result.Failure<MfaSetupDto>(Error.Validation("MFA_NOT_CONFIGURED", "MFA is not enabled on this account."));
        }

        if (!await VerifyStepUpCredentialsAsync(user, session!, currentPassword, currentCode, now, "/api/v1/me/mfa/rotate/setup", cancellationToken))
        {
            var isLocked = user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now;
            return Result.Failure<MfaSetupDto>(isLocked
                ? Error.Unauthorized("MFA_LOCKED_OUT", "Too many failed verification attempts. Try again later.")
                : Error.Unauthorized("MFA_STEP_UP_INVALID", "Current password and valid verification code are required."));
        }

        var secret = _totpService.GenerateBase32Secret();
        user.PendingAuthenticatorKeyEncrypted = _totpService.ProtectSecret(secret);
        user.PendingAuthenticatorKeyExpiresAtUtc = now.Add(PendingMfaSetupLifetime);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<MfaSetupDto>(Error.Conflict("MFA_STATE_CONFLICT", "MFA state changed concurrently. Please try again."));
        }

        await WriteMfaAuditAsync(user, session!, "MfaRotationInitiated", "/api/v1/me/mfa/rotate/setup", "Authenticated MFA secret rotation initiated", cancellationToken);
        return Result.Success(new MfaSetupDto(secret, _totpService.BuildOtpAuthUri("PeopleFlow", user.Email, secret)));
    }

    public async Task<Result<MfaEnableResultDto>> ConfirmMfaRotationAsync(
        Guid userId,
        Guid sessionId,
        string newCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newCode))
        {
            return Result.Failure<MfaEnableResultDto>(Error.Validation("MFA_CODE_REQUIRED", "Verification code is required."));
        }

        if (newCode.Trim().Length > MaxMfaCodeLength)
        {
            return Result.Failure<MfaEnableResultDto>(Error.Unauthorized("MFA_CODE_INVALID", "Invalid verification code."));
        }

        var now = _clock.UtcNow;
        var (user, session, guardError) = await LoadMfaEligibleUserAndSessionAsync(
            userId,
            sessionId,
            requireMfaVerifiedSession: true,
            now,
            cancellationToken);
        if (guardError != null)
        {
            return Result.Failure<MfaEnableResultDto>(guardError);
        }

        if (!user!.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.AuthenticatorKeyEncrypted))
        {
            return Result.Failure<MfaEnableResultDto>(Error.Validation("MFA_NOT_CONFIGURED", "MFA is not enabled on this account."));
        }

        if (string.IsNullOrWhiteSpace(user.PendingAuthenticatorKeyEncrypted) ||
            !user.PendingAuthenticatorKeyExpiresAtUtc.HasValue ||
            user.PendingAuthenticatorKeyExpiresAtUtc.Value <= now)
        {
            if (user.PendingAuthenticatorKeyEncrypted != null || user.PendingAuthenticatorKeyExpiresAtUtc.HasValue)
            {
                user.PendingAuthenticatorKeyEncrypted = null;
                user.PendingAuthenticatorKeyExpiresAtUtc = null;
                await TrySaveChangesAsync(cancellationToken);
            }

            return Result.Failure<MfaEnableResultDto>(Error.Validation("MFA_SETUP_REQUIRED", "Initiate MFA rotation before confirming a new authenticator."));
        }

        var pendingSecret = _totpService.UnprotectSecret(user.PendingAuthenticatorKeyEncrypted);
        if (!VerifyTotpWithReplayProtection(user, pendingSecret, newCode, now, enforceLastStep: false, out var newMatchedStep))
        {
            var lockedOut = await RecordFailedMfaAttemptAsync(user, session!, now, "/api/v1/me/mfa/rotate/confirm", cancellationToken);
            return Result.Failure<MfaEnableResultDto>(lockedOut
                ? Error.Unauthorized("MFA_LOCKED_OUT", "Too many failed verification attempts. Try again later.")
                : Error.Unauthorized("MFA_CODE_INVALID", "Invalid verification code."));
        }

        var plaintextCodes = GenerateRecoveryCodes(8);
        var hashedCodes = plaintextCodes.Select(c => HashSha256Hex(c.ToUpperInvariant())).ToList();

        user.AuthenticatorKeyEncrypted = user.PendingAuthenticatorKeyEncrypted;
        user.PendingAuthenticatorKeyEncrypted = null;
        user.PendingAuthenticatorKeyExpiresAtUtc = null;
        user.LastTotpStepUsed = newMatchedStep;
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.RecoveryCodesHashJson = JsonSerializer.Serialize(hashedCodes);
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        session!.MfaVerifiedAtUtc = now;
        session.LastSeenAtUtc = now;
        await RevokeOtherActiveSessionsAsync(user.Id, session.Id, now, "MfaRotated", cancellationToken);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<MfaEnableResultDto>(Error.Conflict("MFA_STATE_CONFLICT", "MFA state changed concurrently. Please try again."));
        }

        await WriteMfaAuditAsync(user, session, "MfaRotated", "/api/v1/me/mfa/rotate/confirm", "Authenticated MFA secret rotated", cancellationToken);
        return Result.Success(new MfaEnableResultDto(plaintextCodes, BuildVerifiedLoginResult(user, session)));
    }

    public async Task<Result<IReadOnlyList<string>>> RegenerateRecoveryCodesAsync(
        Guid userId,
        Guid sessionId,
        string currentPassword,
        string currentCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(currentCode))
        {
            return Result.Failure<IReadOnlyList<string>>(Error.Validation("MFA_STEP_UP_REQUIRED", "Current password and verification code are required."));
        }

        if (currentPassword.Length > PasswordHasherService.MaxPasswordLength || currentCode.Trim().Length > MaxMfaCodeLength)
        {
            return Result.Failure<IReadOnlyList<string>>(Error.Unauthorized("MFA_STEP_UP_INVALID", "Current password and valid verification code are required."));
        }

        var now = _clock.UtcNow;
        var (user, session, guardError) = await LoadMfaEligibleUserAndSessionAsync(
            userId,
            sessionId,
            requireMfaVerifiedSession: true,
            now,
            cancellationToken);
        if (guardError != null)
        {
            return Result.Failure<IReadOnlyList<string>>(guardError);
        }

        if (!user!.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.AuthenticatorKeyEncrypted))
        {
            return Result.Failure<IReadOnlyList<string>>(Error.Validation("MFA_NOT_CONFIGURED", "MFA is not enabled on this account."));
        }

        if (!await VerifyStepUpCredentialsAsync(user, session!, currentPassword, currentCode, now, "/api/v1/me/mfa/recovery-codes/regenerate", cancellationToken))
        {
            var isLocked = user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now;
            return Result.Failure<IReadOnlyList<string>>(isLocked
                ? Error.Unauthorized("MFA_LOCKED_OUT", "Too many failed verification attempts. Try again later.")
                : Error.Unauthorized("MFA_STEP_UP_INVALID", "Current password and valid verification code are required."));
        }

        var plaintextCodes = GenerateRecoveryCodes(8);
        var hashedCodes = plaintextCodes.Select(c => HashSha256Hex(c.ToUpperInvariant())).ToList();

        user.PendingAuthenticatorKeyEncrypted = null;
        user.PendingAuthenticatorKeyExpiresAtUtc = null;
        user.RecoveryCodesHashJson = JsonSerializer.Serialize(hashedCodes);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        session!.LastSeenAtUtc = now;

        await RevokeOtherActiveSessionsAsync(user.Id, session.Id, now, "MfaRecoveryCodesRegenerated", cancellationToken);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<IReadOnlyList<string>>(Error.Conflict("MFA_STATE_CONFLICT", "MFA state changed concurrently. Please try again."));
        }

        await WriteMfaAuditAsync(user, session, "MfaRecoveryCodesRegenerated", "/api/v1/me/mfa/recovery-codes/regenerate", "Recovery codes regenerated", cancellationToken);
        return Result.Success<IReadOnlyList<string>>(plaintextCodes);
    }

    public async Task<Result> LogoutAsync(Guid sessionId, string reason = "UserLogout", CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.IdentityUserSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session != null && !session.RevokedAtUtc.HasValue)
        {
            session.RevokedAtUtc = _clock.UtcNow;
            session.RevokeReason = reason;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var issued = await IssuePasswordResetTokenInternalAsync(email, cancellationToken);
        if (issued is not null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            var protectedResetToken = _totpService.ProtectOutboxSecret(issued.Value.RawToken);
            var payloadJson = JsonSerializer.Serialize(new
            {
                userId = issued.Value.UserId,
                normalizedEmail = issued.Value.NormalizedEmail,
                protectedResetToken,
                expiresAtUtc = issued.Value.ExpiresAtUtc
            });
            var correlationId = Guid.CreateVersion7();

            if (_dbContext.Database.IsNpgsql() && _outboxPublisher is OutboxPublisher)
            {
                var now = _clock.UtcNow;
                Guid? createdBy = issued.Value.UserId;
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT fn_enqueue_platform_outbox_message({Guid.CreateVersion7()}, {PlatformOutboxMessageTypes.PasswordResetRequested}, CAST({payloadJson} AS jsonb), {correlationId}, {now}, {now}, CAST({createdBy} AS uuid));",
                    cancellationToken);
            }
            else
            {
                await _outboxPublisher.PublishPlatformAsync(
                    messageType: PlatformOutboxMessageTypes.PasswordResetRequested,
                    payloadJson: payloadJson,
                    correlationId: correlationId,
                    cancellationToken: cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<string?> IssuePasswordResetTokenAsync(string email, CancellationToken cancellationToken = default)
    {
        var issued = await IssuePasswordResetTokenInternalAsync(email, cancellationToken);
        if (issued is null)
        {
            return null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return issued.Value.RawToken;
    }

    private async Task<(Guid UserId, string NormalizedEmail, string RawToken, DateTimeOffset ExpiresAtUtc)?> IssuePasswordResetTokenInternalAsync(
        string email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > MaxEmailLength)
        {
            return null;
        }

        var normalized = email.Trim().ToUpperInvariant();
        var user = await _dbContext.IdentityUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized && u.IsActive, cancellationToken);
        if (user == null)
        {
            return null;
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = HashSha256Hex(rawToken);
        var now = _clock.UtcNow;
        var expiresAtUtc = now.AddMinutes(30);

        _dbContext.PasswordResetTokens.Add(new PasswordResetToken(Guid.CreateVersion7())
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc
        });

        return (user.Id, normalized, rawToken, expiresAtUtc);
    }

    public async Task<Result> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) ||
            rawToken.Trim().Length > MaxResetTokenLength ||
            string.IsNullOrWhiteSpace(newPassword) ||
            newPassword.Length < MinPasswordLength ||
            newPassword.Length > PasswordHasherService.MaxPasswordLength)
        {
            return Result.Failure(Error.Validation(
                "PASSWORD_POLICY_FAILED",
                $"Valid token and password ({MinPasswordLength}..{PasswordHasherService.MaxPasswordLength} characters) are required."));
        }

        var hash = HashSha256Hex(rawToken.Trim());
        var now = _clock.UtcNow;

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var token = await _dbContext.PasswordResetTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token == null || token.UsedAtUtc.HasValue || token.ExpiresAtUtc <= now)
        {
            return Result.Failure(Error.Validation("RESET_TOKEN_INVALID", "Password reset token is invalid or expired."));
        }

        var user = await _dbContext.IdentityUsers.SingleOrDefaultAsync(u => u.Id == token.UserId && u.IsActive, cancellationToken);
        if (user == null)
        {
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User account is inactive or missing."));
        }

        var claimed = _dbContext.Database.IsNpgsql()
            ? await _dbContext.PasswordResetTokens
                .Where(t => t.Id == token.Id && t.UsedAtUtc == null && t.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, now), cancellationToken)
            : await _dbContext.PasswordResetTokens
                .Where(t => t.Id == token.Id && t.UsedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, now), cancellationToken);
        if (claimed != 1)
        {
            await tx.RollbackAsync(cancellationToken);
            return Result.Failure(Error.Validation("RESET_TOKEN_INVALID", "Password reset token is invalid or expired."));
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.PendingAuthenticatorKeyEncrypted = null;
        user.PendingAuthenticatorKeyExpiresAtUtc = null;
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _dbContext.IdentityUserSessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokeReason, "PasswordChanged"), cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<UserSessionDto>> ListActiveSessionsAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        return await _dbContext.IdentityUserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .Select(s => new UserSessionDto(
                s.Id,
                s.ActiveTenantId,
                s.SupportGrantId,
                s.MfaVerifiedAtUtc,
                s.IpAddressMasked,
                s.UserAgentSummary,
                s.CreatedAtUtc,
                s.LastSeenAtUtc,
                s.ExpiresAtUtc,
                s.Id == currentSessionId))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result> RevokeSessionAsync(
        Guid userId,
        Guid targetSessionId,
        string reason = "UserRevokedSession",
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.IdentityUserSessions
            .SingleOrDefaultAsync(s => s.Id == targetSessionId && s.UserId == userId, cancellationToken);

        if (session == null)
        {
            return Result.Failure(Error.NotFound("SESSION_NOT_FOUND", "Session not found."));
        }

        if (!session.RevokedAtUtc.HasValue)
        {
            session.RevokedAtUtc = _clock.UtcNow;
            session.RevokeReason = reason;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<AuthenticatedUserProfile?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IdentityUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AuthenticatedUserProfile(
                u.Id,
                u.Email,
                u.PreferredLanguage,
                u.TwoFactorEnabled,
                u.IsPlatformAdmin,
                u.IsBreakGlass,
                u.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AuthenticatedUserProfile?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > MaxEmailLength)
        {
            return null;
        }

        var normalized = email.Trim().ToUpperInvariant();
        return await _dbContext.IdentityUsers.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => new AuthenticatedUserProfile(
                u.Id,
                u.Email,
                u.PreferredLanguage,
                u.TwoFactorEnabled,
                u.IsPlatformAdmin,
                u.IsBreakGlass,
                u.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<AuthenticatedUserProfile>> FindOrCreateUserAsync(
        string email,
        string? initialPassword,
        string preferredLanguage = "id-ID",
        bool isPlatformAdmin = false,
        bool isBreakGlass = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result.Failure<AuthenticatedUserProfile>(Error.Validation("EMAIL_REQUIRED", "Email is required."));
        }

        var trimmedEmail = email.Trim();
        if (trimmedEmail.Length > MaxEmailLength || !MailAddress.TryCreate(trimmedEmail, out var parsedEmail) || !string.Equals(parsedEmail.Address, trimmedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<AuthenticatedUserProfile>(Error.Validation("INVALID_EMAIL", "A valid email address (<=256 characters) is required."));
        }

        var normalized = trimmedEmail.ToUpperInvariant();
        var existing = await _dbContext.IdentityUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
        if (existing != null)
        {
            return Result.Success(new AuthenticatedUserProfile(
                existing.Id,
                existing.Email,
                existing.PreferredLanguage,
                existing.TwoFactorEnabled,
                existing.IsPlatformAdmin,
                existing.IsBreakGlass,
                existing.IsActive));
        }

        if (string.IsNullOrWhiteSpace(initialPassword) ||
            initialPassword.Length < MinPasswordLength ||
            initialPassword.Length > PasswordHasherService.MaxPasswordLength)
        {
            return Result.Failure<AuthenticatedUserProfile>(Error.Validation(
                "PASSWORD_POLICY_FAILED",
                $"New user password must be {MinPasswordLength}..{PasswordHasherService.MaxPasswordLength} characters."));
        }

        var sanitizedLanguage = string.IsNullOrWhiteSpace(preferredLanguage)
            ? "id-ID"
            : preferredLanguage.Trim();
        if (sanitizedLanguage.Length > MaxPreferredLanguageLength)
        {
            sanitizedLanguage = sanitizedLanguage[..MaxPreferredLanguageLength];
        }

        var user = new IdentityUser(Guid.CreateVersion7())
        {
            Email = trimmedEmail,
            NormalizedEmail = normalized,
            PasswordHash = _passwordHasher.HashPassword(initialPassword),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            PreferredLanguage = sanitizedLanguage,
            IsPlatformAdmin = isPlatformAdmin,
            IsBreakGlass = isBreakGlass,
            IsActive = true
        };

        _dbContext.IdentityUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthenticatedUserProfile(
            user.Id,
            user.Email,
            user.PreferredLanguage,
            user.TwoFactorEnabled,
            user.IsPlatformAdmin,
            user.IsBreakGlass,
            user.IsActive));
    }

    private async Task<(IdentityUser? User, IdentityUserSession? Session, Error? Error)> LoadMfaEligibleUserAndSessionAsync(
        Guid userId,
        Guid sessionId,
        bool requireMfaVerifiedSession,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.IdentityUsers.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        var session = await _dbContext.IdentityUserSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);

        if (user == null || session == null || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= now)
        {
            return (null, null, Error.Unauthorized("SESSION_INVALID", "Session is missing, expired, or revoked."));
        }

        if (session.SupportGrantId.HasValue)
        {
            return (null, null, Error.Forbidden("SUPPORT_ACCESS_READ_ONLY", "Support access sessions cannot modify authentication settings."));
        }

        if (requireMfaVerifiedSession && !session.MfaVerifiedAtUtc.HasValue)
        {
            return (null, null, Error.Forbidden("MFA_VERIFICATION_REQUIRED", "Multi-factor authentication verification is required."));
        }

        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now)
        {
            return (null, null, Error.Unauthorized("MFA_LOCKED_OUT", "Too many failed verification attempts. Try again later."));
        }

        return (user, session, null);
    }

    private async Task<bool> VerifyStepUpCredentialsAsync(
        IdentityUser user,
        IdentityUserSession session,
        string currentPassword,
        string currentCode,
        DateTimeOffset now,
        string resourcePath,
        CancellationToken cancellationToken)
    {
        var passwordValid = _passwordHasher.VerifyPassword(user.PasswordHash, currentPassword);
        var currentSecret = _totpService.UnprotectSecret(user.AuthenticatorKeyEncrypted!);
        var totpValid = VerifyTotpWithReplayProtection(user, currentSecret, currentCode, now, enforceLastStep: true, out var matchedStep);

        if (!passwordValid || !totpValid)
        {
            await RecordFailedMfaAttemptAsync(user, session, now, resourcePath, cancellationToken);
            return false;
        }

        user.LastTotpStepUsed = matchedStep;
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        return true;
    }

    private async Task<bool> RecordFailedMfaAttemptAsync(
        IdentityUser user,
        IdentityUserSession session,
        DateTimeOffset now,
        string resourcePath,
        CancellationToken cancellationToken)
    {
        var lockedOut = await IncrementFailedAttemptsAtomicAsync(user, now, cancellationToken);
        if (lockedOut)
        {
            await _dbContext.IdentityUserSessions
                .Where(s => s.Id == session.Id && s.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokeReason, "MfaLockout"), cancellationToken);
            session.RevokedAtUtc = now;
            session.RevokeReason = "MfaLockout";
            await WriteMfaAuditAsync(user, session, "MfaVerificationLockedOut", resourcePath, "Exceeded max failed MFA attempts", cancellationToken);
        }

        return lockedOut;
    }

    private async Task<bool> IncrementFailedAttemptsAtomicAsync(
        IdentityUser user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _dbContext.IdentityUsers
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount + 1), cancellationToken);

        var lockoutUntil = now.Add(LockoutDuration);
        if (_dbContext.Database.IsNpgsql())
        {
            await _dbContext.IdentityUsers
                .Where(u => u.Id == user.Id &&
                            u.AccessFailedCount >= MaxFailedAttempts &&
                            (u.LockoutEndUtc == null || u.LockoutEndUtc <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEndUtc, lockoutUntil), cancellationToken);
        }
        else
        {
            var currentLockout = await _dbContext.IdentityUsers
                .AsNoTracking()
                .Where(u => u.Id == user.Id)
                .Select(u => new { u.AccessFailedCount, u.LockoutEndUtc })
                .SingleAsync(cancellationToken);

            if (currentLockout.AccessFailedCount >= MaxFailedAttempts &&
                (!currentLockout.LockoutEndUtc.HasValue || currentLockout.LockoutEndUtc.Value <= now))
            {
                await _dbContext.IdentityUsers
                    .Where(u => u.Id == user.Id && u.AccessFailedCount >= MaxFailedAttempts)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEndUtc, lockoutUntil), cancellationToken);
            }
        }

        var fresh = await _dbContext.IdentityUsers
            .AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => new { u.AccessFailedCount, u.LockoutEndUtc })
            .SingleAsync(cancellationToken);

        await _dbContext.Entry(user).ReloadAsync(cancellationToken);
        return fresh.LockoutEndUtc.HasValue && fresh.LockoutEndUtc.Value > now;
    }

    private bool VerifyTotpWithReplayProtection(
        IdentityUser user,
        string base32Secret,
        string code,
        DateTimeOffset now,
        bool enforceLastStep,
        out long matchedStep)
    {
        if (!_totpService.VerifyCode(base32Secret, code, now, out matchedStep))
        {
            return false;
        }

        if (enforceLastStep && user.LastTotpStepUsed.HasValue && matchedStep <= user.LastTotpStepUsed.Value)
        {
            return false;
        }

        return true;
    }

    private async Task RevokeOtherActiveSessionsAsync(
        Guid userId,
        Guid exceptSessionId,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken)
    {
        var activeSessions = await _dbContext.IdentityUserSessions
            .Where(s => s.UserId == userId && s.Id != exceptSessionId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var activeSession in activeSessions)
        {
            activeSession.RevokedAtUtc = now;
            activeSession.RevokeReason = reason;
        }
    }

    private static LoginResultDto BuildVerifiedLoginResult(IdentityUser user, IdentityUserSession session) =>
        new(
            session.ActiveTenantId.HasValue || user.IsPlatformAdmin
                ? LoginOutcomeStatus.Authenticated
                : LoginOutcomeStatus.TenantSelectionRequired,
            user.Id,
            session.Id,
            session.ActiveTenantId,
            user.IsPlatformAdmin,
            user.IsBreakGlass,
            MfaVerified: true);

    private async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private Task WriteMfaAuditAsync(
        IdentityUser user,
        IdentityUserSession session,
        string action,
        string resourcePath,
        string? reason,
        CancellationToken cancellationToken) =>
        _platformAuditLogWriter.WriteAsync(new PlatformAuditEntry(
            ActorUserId: user.Id,
            ActorRole: user.IsBreakGlass ? "BreakGlass" : user.IsPlatformAdmin ? "PlatformAdmin" : "User",
            Action: action,
            TargetTenantId: session.ActiveTenantId,
            ResourcePath: resourcePath,
            Reason: reason), cancellationToken);

    private static List<string> GenerateRecoveryCodes(int count)
    {
        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToUpperInvariant();
            list.Add($"{raw[..5]}-{raw[5..]}");
        }
        return list;
    }

    public static string HashSha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private static string? MaskIpAddress(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var trimmed = ip.Trim();
        if (!IPAddress.TryParse(trimmed, out var parsed))
        {
            return "[redacted-ip]";
        }

        if (parsed.IsIPv4MappedToIPv6)
        {
            parsed = parsed.MapToIPv4();
        }

        var bytes = parsed.GetAddressBytes();
        if (parsed.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.xxx";
        }

        if (parsed.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            Array.Clear(bytes, 6, 10);
            return $"{new IPAddress(bytes)}/48";
        }

        return "[redacted-ip]";
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
