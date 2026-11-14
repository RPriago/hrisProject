using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Outbox;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class InvitationService : IInvitationService
{
    private sealed class InvitationLookupRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string InvitedEmail { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    private readonly TenancyDbContext _db;
    private readonly IIdentityProvisioningService _identityProvisioning;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly ISubscriptionQuotaService _quotaService;
    private readonly IActiveHeadcountCounter _headcountCounter;
    private readonly IOrganizationUnitValidator _orgValidator;
    private readonly IOutboxPublisher _outbox;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IClock _clock;

    public InvitationService(
        TenancyDbContext db,
        IIdentityProvisioningService identityProvisioning,
        IPermissionAndScopeResolver permissionResolver,
        ISubscriptionQuotaService quotaService,
        IActiveHeadcountCounter headcountCounter,
        IOrganizationUnitValidator orgValidator,
        IOutboxPublisher outbox,
        IPlatformAuditLogWriter auditWriter,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _identityProvisioning = identityProvisioning ?? throw new ArgumentNullException(nameof(identityProvisioning));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _quotaService = quotaService ?? throw new ArgumentNullException(nameof(quotaService));
        _headcountCounter = headcountCounter ?? throw new ArgumentNullException(nameof(headcountCounter));
        _orgValidator = orgValidator ?? throw new ArgumentNullException(nameof(orgValidator));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<InvitationCreatedDto>> CreateInvitationAsync(
        Guid tenantId,
        Guid actorUserId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            return Result.Failure<InvitationCreatedDto>(new Error("INVALID_EMAIL", "A valid email address is required."));
        }

        using var _ = _db.BeginOperationTenantScope(tenantId);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var createResult = await TenantTransactionSerializer.ExecuteAsync(_db, tenantId, async () =>
        {
            var tenant = await _db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
            if (tenant is null || !string.Equals(tenant.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<InvitationCreatedDto>(Error.Forbidden("TENANT_NOT_ACTIVE", "Invitations cannot be created for a non-active tenant."));
            }

            var roleCheck = await RoleScopeAssignmentValidator.ValidateAssignmentsAsync(
                _db, _permissionResolver, _orgValidator, tenantId, actorUserId, request.RoleAssignments, cancellationToken);
            if (!roleCheck.IsSuccess)
            {
                return Result.Failure<InvitationCreatedDto>(roleCheck.Error);
            }

            var currentActiveEmployees = await _headcountCounter.CountActiveEmployeesAsync(tenantId, cancellationToken);
            var quotaCheck = await _quotaService.CheckEmployeeQuotaAsync(tenantId, currentActiveEmployees, cancellationToken);
            if (!quotaCheck.IsSuccess)
            {
                return Result.Failure<InvitationCreatedDto>(quotaCheck.Error);
            }

            var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
            var rawToken = Convert.ToBase64String(rawTokenBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var tokenHash = ComputeSha256Hex(rawToken);
            var expiresAt = _clock.UtcNow.AddDays(Math.Clamp(request.ExpiresInDays, 1, 30));

            var invitation = new TenancyInvitation(Guid.CreateVersion7())
            {
                TenantId = tenantId,
                InvitedEmail = normalizedEmail,
                TargetEmployeeId = request.TargetEmployeeId,
                InitialRoleAssignmentsJson = JsonSerializer.Serialize(request.RoleAssignments),
                TokenHash = tokenHash,
                Status = "Pending",
                ExpiresAtUtc = expiresAt,
                InvitedByUserId = actorUserId
            };

            _db.TenancyInvitations.Add(invitation);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new InvitationCreatedDto(invitation.Id, normalizedEmail, rawToken, expiresAt));
        }, cancellationToken);

        if (!createResult.IsSuccess)
        {
            return createResult;
        }

        var dto = createResult.Value;
        await _outbox.PublishRawAsync(
            "Tenancy.InvitationCreated",
            JsonSerializer.Serialize(new { dto.InvitationId, tenantId, invitedEmail = normalizedEmail, expiresAtUtc = dto.ExpiresAtUtc }),
            tenantId: tenantId,
            autoSave: true,
            cancellationToken: cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            actorUserId,
            "TenantMember",
            "TENANCY_INVITATION_CREATED",
            TargetTenantId: tenantId,
            ResourcePath: "/api/v1/settings/invitations",
            MetadataJson: JsonSerializer.Serialize(new { invitationId = dto.InvitationId, invitedEmail = normalizedEmail })), cancellationToken);

        return createResult;
    }

    public async Task<IReadOnlyList<InvitationDto>> ListInvitationsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var items = await _db.TenancyInvitations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .Select(i => new InvitationDto(i.Id, i.InvitedEmail, i.Status, i.ExpiresAtUtc, i.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return items.OrderByDescending(i => i.CreatedAtUtc).ToList();
    }

    public async Task<Result<AcceptInvitationResultDto>> AcceptInvitationAsync(
        AcceptInvitationRequest request,
        Guid? authenticatedUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Result.Failure<AcceptInvitationResultDto>(new Error("INVITATION_INVALID_OR_EXPIRED", "Invitation token is required."));
        }

        var tokenHash = ComputeSha256Hex(request.Token.Trim());
        var lookup = await LookupByTokenHashAsync(tokenHash, cancellationToken);
        var now = _clock.UtcNow;

        if (lookup is null || !lookup.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || lookup.ExpiresAtUtc <= now)
        {
            return Result.Failure<AcceptInvitationResultDto>(new Error("INVITATION_INVALID_OR_EXPIRED", "Invitation token is invalid, expired, or already used."));
        }

        using var _ = _db.BeginOperationTenantScope(lookup.TenantId);

        var tenant = await _db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == lookup.TenantId, cancellationToken);
        if (tenant is null || !string.Equals(tenant.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<AcceptInvitationResultDto>(Error.Forbidden("TENANT_NOT_ACTIVE", "Invitations cannot be accepted for a non-active tenant."));
        }

        var preCheckInvitation = await _db.TenancyInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == lookup.TenantId && i.Id == lookup.Id, cancellationToken);
        if (preCheckInvitation is null || preCheckInvitation.Status != "Pending" || preCheckInvitation.ExpiresAtUtc <= now)
        {
            return Result.Failure<AcceptInvitationResultDto>(new Error("INVITATION_INVALID_OR_EXPIRED", "Invitation token is invalid or expired."));
        }

        var existingUser = await _identityProvisioning.FindByEmailAsync(lookup.InvitedEmail, cancellationToken);
        bool newAccountCreated = false;
        Guid userId;

        if (existingUser is not null)
        {
            if (!authenticatedUserId.HasValue)
            {
                return Result.Failure<AcceptInvitationResultDto>(Error.Unauthorized("INVITATION_AUTH_REQUIRED", "Existing account holders must authenticate before accepting an invitation."));
            }

            if (authenticatedUserId.Value != existingUser.UserId)
            {
                return Result.Failure<AcceptInvitationResultDto>(Error.Forbidden("EMAIL_MISMATCH", "Authenticated user does not match the invited email."));
            }

            if (!existingUser.IsActive)
            {
                return Result.Failure<AcceptInvitationResultDto>(Error.Forbidden("USER_INACTIVE", "Inactive user accounts cannot accept tenant invitations."));
            }

            userId = existingUser.UserId;
        }
        else
        {
            if (authenticatedUserId.HasValue)
            {
                return Result.Failure<AcceptInvitationResultDto>(Error.Forbidden("EMAIL_MISMATCH", "Authenticated account does not match invited email."));
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return Result.Failure<AcceptInvitationResultDto>(new Error("PASSWORD_REQUIRED", "A password is required to create your account."));
            }

            var created = await _identityProvisioning.FindOrCreateUserAsync(
                lookup.InvitedEmail,
                request.NewPassword,
                string.IsNullOrWhiteSpace(request.PreferredLanguage) ? "id-ID" : request.PreferredLanguage.Trim(),
                cancellationToken: cancellationToken);
            if (!created.IsSuccess)
            {
                return Result.Failure<AcceptInvitationResultDto>(created.Error);
            }

            userId = created.Value.UserId;
            newAccountCreated = true;
        }

        var acceptResult = await TenantTransactionSerializer.ExecuteAsync(_db, lookup.TenantId, async () =>
        {
            var tenantStillActive = await _db.Tenants.AsNoTracking()
                .AnyAsync(t => t.Id == lookup.TenantId && t.Status == "Active", cancellationToken);
            if (!tenantStillActive)
            {
                return Result.Failure<AcceptInvitationResultDto>(Error.Forbidden("TENANT_NOT_ACTIVE", "Invitations cannot be accepted for a non-active tenant."));
            }

            var invitation = await _db.TenancyInvitations
                .FirstOrDefaultAsync(i => i.TenantId == lookup.TenantId && i.Id == lookup.Id, cancellationToken);
            if (invitation is null || invitation.Status != "Pending" || invitation.ExpiresAtUtc <= _clock.UtcNow)
            {
                return Result.Failure<AcceptInvitationResultDto>(new Error("INVITATION_INVALID_OR_EXPIRED", "Invitation token is invalid or expired."));
            }

            var currentActiveEmployees = await _headcountCounter.CountActiveEmployeesAsync(lookup.TenantId, cancellationToken);
            var quotaCheck = await _quotaService.CheckEmployeeQuotaAsync(lookup.TenantId, currentActiveEmployees, cancellationToken);
            if (!quotaCheck.IsSuccess)
            {
                return Result.Failure<AcceptInvitationResultDto>(quotaCheck.Error);
            }

            var existingMembership = await _db.TenancyMemberships
                .FirstOrDefaultAsync(m => m.TenantId == lookup.TenantId && m.UserId == userId, cancellationToken);

            Guid membershipId;
            if (existingMembership is not null)
            {
                if (existingMembership.Status == "Active")
                {
                    return Result.Failure<AcceptInvitationResultDto>(Error.Conflict("ALREADY_ACTIVE_MEMBER", "User is already an active member of this tenant."));
                }

                if (existingMembership.RevokedAtUtc.HasValue && invitation.CreatedAtUtc <= existingMembership.RevokedAtUtc.Value)
                {
                    return Result.Failure<AcceptInvitationResultDto>(Error.Conflict("INVITATION_REVOKED", "Invitation predates membership revocation and cannot reactivate access."));
                }

                existingMembership.Status = "Active";
                existingMembership.RevokedAtUtc = null;
                existingMembership.RevokeReason = null;
                existingMembership.JoinedAtUtc = now;
                membershipId = existingMembership.Id;

                var oldScopes = await _db.TenancyMemberRoleScopes
                    .Where(s => s.TenantId == lookup.TenantId && s.MembershipId == membershipId)
                    .ToListAsync(cancellationToken);
                _db.TenancyMemberRoleScopes.RemoveRange(oldScopes);
            }
            else
            {
                membershipId = Guid.CreateVersion7();
                _db.TenancyMemberships.Add(new TenancyMembership(membershipId)
                {
                    TenantId = lookup.TenantId,
                    UserId = userId,
                    Status = "Active",
                    JoinedAtUtc = now
                });
            }

            var assignments = JsonSerializer.Deserialize<List<RoleScopeAssignmentInput>>(invitation.InitialRoleAssignmentsJson)
                ?? new List<RoleScopeAssignmentInput>();

            foreach (var a in assignments)
            {
                _db.TenancyMemberRoleScopes.Add(new TenancyMemberRoleScope(Guid.CreateVersion7())
                {
                    TenantId = lookup.TenantId,
                    MembershipId = membershipId,
                    RoleId = a.RoleId,
                    ScopeType = a.ScopeType,
                    ScopeTargetIdsJson = a.ScopeTargetIds is { Count: > 0 } ? JsonSerializer.Serialize(a.ScopeTargetIds) : null,
                    DataPeriodFrom = a.DataPeriodFrom,
                    DataPeriodTo = a.DataPeriodTo,
                    ValidUntilUtc = a.ValidUntilUtc
                });
            }

            invitation.Status = "Accepted";
            invitation.AcceptedByUserId = userId;

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(new AcceptInvitationResultDto(lookup.TenantId, membershipId, userId, newAccountCreated));
        }, cancellationToken);

        if (!acceptResult.IsSuccess)
        {
            return acceptResult;
        }

        var accepted = acceptResult.Value;
        await _outbox.PublishRawAsync(
            "Tenancy.InvitationAccepted",
            JsonSerializer.Serialize(new { lookup.Id, tenantId = lookup.TenantId, userId, membershipId = accepted.MembershipId }),
            tenantId: lookup.TenantId,
            autoSave: true,
            cancellationToken: cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            userId,
            "TenantMember",
            "TENANCY_INVITATION_ACCEPTED",
            TargetTenantId: lookup.TenantId,
            ResourcePath: "/api/v1/invitations/accept",
            MetadataJson: JsonSerializer.Serialize(new { invitationId = lookup.Id, membershipId = accepted.MembershipId })), cancellationToken);

        return acceptResult;
    }

    private async Task<InvitationLookupRow?> LookupByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        if (_db.Database.IsNpgsql())
        {
            return await _db.Database
                .SqlQueryRaw<InvitationLookupRow>(
                    """
                    SELECT id AS "Id",
                           tenant_id AS "TenantId",
                           invited_email AS "InvitedEmail",
                           status AS "Status",
                           expires_at_utc AS "ExpiresAtUtc"
                    FROM fn_lookup_invitation_by_token_hash({0})
                    """,
                    tokenHash)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _db.TenancyInvitations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TokenHash == tokenHash)
            .Select(i => new InvitationLookupRow
            {
                Id = i.Id,
                TenantId = i.TenantId,
                InvitedEmail = i.InvitedEmail,
                Status = i.Status,
                ExpiresAtUtc = i.ExpiresAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string ComputeSha256Hex(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
