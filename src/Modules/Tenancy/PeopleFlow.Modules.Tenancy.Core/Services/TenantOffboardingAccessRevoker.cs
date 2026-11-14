using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Outbox;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class TenantOffboardingAccessRevoker : ITenantOffboardingAccessRevoker
{
    private readonly TenancyDbContext _db;
    private readonly IUserSessionRevoker _sessionRevoker;
    private readonly IOutboxPublisher _outbox;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IIdentityAuthService? _identityAuth;

    public TenantOffboardingAccessRevoker(
        TenancyDbContext db,
        IUserSessionRevoker sessionRevoker,
        IOutboxPublisher outbox,
        IPlatformAuditLogWriter auditWriter,
        IClock clock,
        IIdentityAuthService? identityAuth = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _sessionRevoker = sessionRevoker ?? throw new ArgumentNullException(nameof(sessionRevoker));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _identityAuth = identityAuth;
    }

    public async Task<Result> RevokeOnOffboardingAsync(
        Guid tenantId,
        Guid userId,
        string reason = "TenantOffboarding",
        CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var membership = await _db.TenancyMemberships
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);

        if (membership is not null && membership.Status != "Revoked")
        {
            membership.Status = "Revoked";
            membership.RevokedAtUtc = _clock.UtcNow;
            membership.RevokeReason = string.IsNullOrWhiteSpace(reason) ? "TenantOffboarding" : reason.Trim();
        }

        await PendingInvitationRevoker.RevokePendingForUserAsync(_db, _identityAuth, tenantId, userId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var revokedCount = await _sessionRevoker.RevokeUserTenantSessionsAsync(tenantId, userId, reason, cancellationToken);

        await _outbox.PublishRawAsync(
            "Tenancy.MemberOffboarded",
            JsonSerializer.Serialize(new { tenantId, userId, reason, revokedSessionCount = revokedCount }),
            tenantId: tenantId,
            autoSave: true,
            cancellationToken: cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            null,
            "System",
            "TENANCY_OFFBOARDING_ACCESS_REVOKED",
            TargetTenantId: tenantId,
            ResourcePath: "offboarding",
            Reason: reason,
            MetadataJson: JsonSerializer.Serialize(new { targetUserId = userId, revokedSessionCount = revokedCount })), cancellationToken);

        return Result.Success();
    }
}
