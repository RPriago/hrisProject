using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Outbox;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class SupportAccessGrantService : ISupportAccessGrantService
{
    private readonly TenancyDbContext _db;
    private readonly PeopleFlowDbContext _infraDb;
    private readonly IIdentityAuthService _identityAuth;
    private readonly ISessionContextResolver _sessionResolver;
    private readonly ITenantMembershipReader _membershipReader;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IOutboxPublisher _outbox;
    private readonly IClock _clock;

    public SupportAccessGrantService(
        TenancyDbContext db,
        PeopleFlowDbContext infraDb,
        IIdentityAuthService identityAuth,
        ISessionContextResolver sessionResolver,
        ITenantMembershipReader membershipReader,
        IPlatformAuditLogWriter auditWriter,
        IOutboxPublisher outbox,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _infraDb = infraDb ?? throw new ArgumentNullException(nameof(infraDb));
        _identityAuth = identityAuth ?? throw new ArgumentNullException(nameof(identityAuth));
        _sessionResolver = sessionResolver ?? throw new ArgumentNullException(nameof(sessionResolver));
        _membershipReader = membershipReader ?? throw new ArgumentNullException(nameof(membershipReader));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<SupportAccessGrantDto>> ListGrantsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var items = await _db.SupportAccessGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return items
            .OrderByDescending(g => g.CreatedAtUtc)
            .Select(MapDto)
            .ToList();
    }

    public async Task<Result<SupportAccessGrantDto>> CreateGrantAsync(
        Guid tenantId,
        Guid ownerUserId,
        CreateSupportGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.TicketReference) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure<SupportAccessGrantDto>(new Error("INVALID_SUPPORT_GRANT", "TicketReference and Reason are required."));
        }

        if (!string.Equals(request.AccessLevel, "ReadOnly", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<SupportAccessGrantDto>(new Error("INVALID_ACCESS_LEVEL", "Support access grants must use ReadOnly access level."));
        }

        var tenant = await _db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<SupportAccessGrantDto>(new Error("TENANT_NOT_FOUND", "Tenant was not found."));
        }

        if (!string.Equals(tenant.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<SupportAccessGrantDto>(Error.Forbidden("TENANT_NOT_ACTIVE", "Support access grants cannot be created for a non-active tenant."));
        }

        if (request.AssignedAdminUserId.HasValue)
        {
            if (request.AssignedAdminUserId.Value == Guid.Empty)
            {
                return Result.Failure<SupportAccessGrantDto>(Error.Validation("INVALID_ASSIGNED_ADMIN", "Assigned support user must be an active PlatformAdmin."));
            }

            var adminProfile = await _identityAuth.GetUserProfileAsync(request.AssignedAdminUserId.Value, cancellationToken);
            if (adminProfile is null || !adminProfile.IsActive || !adminProfile.IsPlatformAdmin)
            {
                return Result.Failure<SupportAccessGrantDto>(Error.Validation("INVALID_ASSIGNED_ADMIN", "Assigned support user must be an active PlatformAdmin."));
            }
        }

        var now = _clock.UtcNow;
        var duration = Math.Clamp(request.DurationMinutes, 5, 240);

        var grant = new SupportAccessGrant(Guid.CreateVersion7())
        {
            TenantId = tenantId,
            TicketReference = request.TicketReference.Trim(),
            Reason = request.Reason.Trim(),
            AccessLevel = "ReadOnly",
            AllowFinancialRead = request.AllowFinancialRead,
            ApprovedByUserId = ownerUserId,
            AssignedAdminUserId = request.AssignedAdminUserId,
            StartsAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(duration)
        };

        _db.SupportAccessGrants.Add(grant);
        await _db.SaveChangesAsync(cancellationToken);

        await _outbox.PublishRawAsync(
            "Tenancy.SupportGrantCreated",
            JsonSerializer.Serialize(new { grant.Id, tenantId, grant.TicketReference, grant.ExpiresAtUtc }),
            tenantId: tenantId,
            autoSave: true,
            cancellationToken: cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            ownerUserId,
            "CompanyOwner",
            "SUPPORT_GRANT_CREATED",
            TargetTenantId: tenantId,
            SupportGrantId: grant.Id,
            ResourcePath: "/api/v1/settings/support-grants",
            Reason: grant.Reason,
            MetadataJson: JsonSerializer.Serialize(new { grant.TicketReference, grant.AllowFinancialRead, grant.ExpiresAtUtc })), cancellationToken);

        return Result.Success(MapDto(grant));
    }

    public async Task<Result> RevokeGrantAsync(Guid tenantId, Guid actorUserId, Guid grantId, CancellationToken cancellationToken = default)
    {
        var grant = await _db.SupportAccessGrants
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == grantId, cancellationToken);
        if (grant is null)
        {
            return Result.Failure(new Error("GRANT_NOT_FOUND", "Support access grant was not found."));
        }

        if (!grant.RevokedAtUtc.HasValue)
        {
            grant.RevokedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await _sessionResolver.ClearSupportGrantSessionsAsync(grant.Id, cancellationToken);

            await _auditWriter.WriteAsync(new PlatformAuditEntry(
                actorUserId,
                "CompanyOwner",
                "SUPPORT_GRANT_REVOKED",
                TargetTenantId: tenantId,
                SupportGrantId: grant.Id,
                ResourcePath: $"/api/v1/settings/support-grants/{grantId}"), cancellationToken);
        }
        else
        {
            await _sessionResolver.ClearSupportGrantSessionsAsync(grant.Id, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<SupportSessionEnteredDto>> EnterSupportSessionAsync(
        Guid adminUserId,
        Guid sessionId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _identityAuth.GetUserProfileAsync(adminUserId, cancellationToken);
        if (profile is null || !profile.IsActive || !profile.IsPlatformAdmin)
        {
            return Result.Failure<SupportSessionEnteredDto>(new Error("FORBIDDEN", "Only active PlatformAdmin accounts may enter support sessions."));
        }

        var session = await _sessionResolver.ResolveActiveSessionAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != adminUserId)
        {
            return Result.Failure<SupportSessionEnteredDto>(new Error("SESSION_INVALID", "Active session is invalid."));
        }

        if (!session.IsMfaVerified)
        {
            return Result.Failure<SupportSessionEnteredDto>(new Error("MFA_REQUIRED", "PlatformAdmin session must complete MFA verification before entering support mode."));
        }

        var grant = await _membershipReader.GetActiveSupportGrantAsync(grantId, adminUserId, cancellationToken);
        if (grant is null)
        {
            return Result.Failure<SupportSessionEnteredDto>(new Error("GRANT_INVALID_OR_EXPIRED", "Support access grant is invalid, unassigned, expired, or revoked."));
        }

        var setResult = await _sessionResolver.SetSupportGrantAsync(sessionId, grant.GrantId, grant.TenantId, cancellationToken);
        if (!setResult.IsSuccess)
        {
            return Result.Failure<SupportSessionEnteredDto>(setResult.Error);
        }

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            adminUserId,
            "PlatformAdmin",
            "SUPPORT_SESSION_ENTERED",
            TargetTenantId: grant.TenantId,
            SupportGrantId: grant.GrantId,
            ResourcePath: "/api/v1/platform/support-sessions/enter"), cancellationToken);

        return Result.Success(new SupportSessionEnteredDto(
            grant.GrantId,
            grant.TenantId,
            grant.AccessLevel,
            grant.AllowFinancialRead,
            grant.ExpiresAtUtc));
    }

    public async Task<Result> ExitSupportSessionAsync(Guid adminUserId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _sessionResolver.ResolveActiveSessionAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != adminUserId)
        {
            return Result.Failure(new Error("SESSION_INVALID", "Active session is invalid."));
        }

        var prevGrantId = session.SupportGrantId;
        var prevTenantId = session.ActiveTenantId;

        var setResult = await _sessionResolver.SetSupportGrantAsync(sessionId, null, null, cancellationToken);
        if (!setResult.IsSuccess)
        {
            return setResult;
        }

        if (prevGrantId.HasValue)
        {
            await _auditWriter.WriteAsync(new PlatformAuditEntry(
                adminUserId,
                "PlatformAdmin",
                "SUPPORT_SESSION_EXITED",
                TargetTenantId: prevTenantId,
                SupportGrantId: prevGrantId,
                ResourcePath: "/api/v1/platform/support-sessions/exit"), cancellationToken);
        }

        return Result.Success();
    }

    public async Task<IReadOnlyList<PlatformAuditLogDto>> ListTenantAuditLogsAsync(
        Guid tenantId,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var bounded = Math.Clamp(take, 1, 500);
        var logs = await _infraDb.PlatformAuditLogs
            .AsNoTracking()
            .Where(l => l.TargetTenantId == tenantId)
            .ToListAsync(cancellationToken);

        return logs
            .OrderByDescending(l => l.OccurredAtUtc)
            .Take(bounded)
            .Select(l => new PlatformAuditLogDto(
                l.Id,
                l.ActorUserId,
                l.ActorRole,
                l.Action,
                l.TargetTenantId,
                l.SupportGrantId,
                l.ResourcePath,
                l.CorrelationId,
                l.Reason,
                l.OccurredAtUtc))
            .ToList();
    }

    private static SupportAccessGrantDto MapDto(SupportAccessGrant g) =>
        new(
            g.Id,
            g.TenantId,
            g.TicketReference,
            g.Reason,
            g.AccessLevel,
            g.AllowFinancialRead,
            g.ApprovedByUserId,
            g.AssignedAdminUserId,
            g.StartsAtUtc,
            g.ExpiresAtUtc,
            g.RevokedAtUtc);
}
