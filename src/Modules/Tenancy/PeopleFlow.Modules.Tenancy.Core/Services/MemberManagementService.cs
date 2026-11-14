using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Entities;
using PeopleFlow.Modules.Tenancy.Core.Persistence;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Outbox;
using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class MemberManagementService : IMemberManagementService
{
    private readonly TenancyDbContext _db;
    private readonly IPermissionAndScopeResolver _permissionResolver;
    private readonly IOrganizationUnitValidator _orgValidator;
    private readonly IUserSessionRevoker _sessionRevoker;
    private readonly IOutboxPublisher _outbox;
    private readonly IPlatformAuditLogWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IIdentityAuthService? _identityAuth;

    public MemberManagementService(
        TenancyDbContext db,
        IPermissionAndScopeResolver permissionResolver,
        IOrganizationUnitValidator orgValidator,
        IUserSessionRevoker sessionRevoker,
        IOutboxPublisher outbox,
        IPlatformAuditLogWriter auditWriter,
        IClock clock,
        IIdentityAuthService? identityAuth = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _permissionResolver = permissionResolver ?? throw new ArgumentNullException(nameof(permissionResolver));
        _orgValidator = orgValidator ?? throw new ArgumentNullException(nameof(orgValidator));
        _sessionRevoker = sessionRevoker ?? throw new ArgumentNullException(nameof(sessionRevoker));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _identityAuth = identityAuth;
    }

    public async Task<IReadOnlyList<TenantMemberDto>> ListMembersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var memberships = (await _db.TenancyMemberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .ToListAsync(cancellationToken))
            .OrderBy(m => m.JoinedAtUtc)
            .ToList();

        var scopes = await _db.TenancyMemberRoleScopes
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var roles = await _db.TenancyRoles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var scopesByMember = scopes.ToLookup(s => s.MembershipId);

        return memberships.Select(m => MapMemberDto(m, scopesByMember[m.Id], roles)).ToList();
    }

    public async Task<Result<TenantMemberDto>> UpdateMemberRolesAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid targetUserId,
        UpdateMemberRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorPolicy = await _permissionResolver.ResolveAsync(tenantId, actorUserId, cancellationToken);
        var roleCheck = await RoleScopeAssignmentValidator.ValidateAssignmentsAsync(
            _db, _permissionResolver, _orgValidator, tenantId, actorUserId, request.RoleAssignments, cancellationToken, actorPolicy);
        if (!roleCheck.IsSuccess)
        {
            return Result.Failure<TenantMemberDto>(roleCheck.Error);
        }

        using var _ = _db.BeginOperationTenantScope(tenantId);

        var txResult = await ExecuteSerializedTenantTransactionAsync(tenantId, async () =>
        {
            var membership = await _db.TenancyMemberships
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == targetUserId && m.Status == "Active", cancellationToken);
            if (membership is null)
            {
                return Result.Failure<TenantMemberDto>(Error.NotFound("MEMBER_NOT_FOUND", "Active tenant member was not found."));
            }

            var ceilingCheck = await RoleScopeAssignmentValidator.EnsureActorCanManageTargetMemberAsync(
                _db, _permissionResolver, tenantId, actorUserId, membership.Id, _clock.UtcNow, actorPolicy, cancellationToken);
            if (!ceilingCheck.IsSuccess)
            {
                return Result.Failure<TenantMemberDto>(ceilingCheck.Error);
            }

            var ownerRole = await _db.TenancyRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Code == RoleCodes.CompanyOwner, cancellationToken);

            var requestPreservesPermanentTenantOwner = ownerRole is not null && request.RoleAssignments.Any(r =>
                r.RoleId == ownerRole.Id &&
                string.Equals(r.ScopeType, nameof(DataScope.Tenant), StringComparison.OrdinalIgnoreCase) &&
                r.ValidUntilUtc is null);

            if (ownerRole is not null && !requestPreservesPermanentTenantOwner)
            {
                var otherOwnerExists = await OtherActiveOwnerExistsAsync(tenantId, membership.Id, ownerRole.Id, cancellationToken);
                var targetWasOwner = await _db.TenancyMemberRoleScopes
                    .AnyAsync(s => s.TenantId == tenantId && s.MembershipId == membership.Id && s.RoleId == ownerRole.Id, cancellationToken);

                if (targetWasOwner && !otherOwnerExists)
                {
                    return Result.Failure<TenantMemberDto>(Error.Conflict(
                        "LAST_OWNER_PROTECTED",
                        "At least one active, permanent, tenant-wide CompanyOwner must remain."));
                }
            }

            var existingScopes = await _db.TenancyMemberRoleScopes
                .Where(s => s.TenantId == tenantId && s.MembershipId == membership.Id)
                .ToListAsync(cancellationToken);
            _db.TenancyMemberRoleScopes.RemoveRange(existingScopes);

            var newScopes = request.RoleAssignments.Select(a => new TenancyMemberRoleScope(Guid.CreateVersion7())
            {
                TenantId = tenantId,
                MembershipId = membership.Id,
                RoleId = a.RoleId,
                ScopeType = a.ScopeType,
                ScopeTargetIdsJson = a.ScopeTargetIds is { Count: > 0 } ? JsonSerializer.Serialize(a.ScopeTargetIds) : null,
                DataPeriodFrom = a.DataPeriodFrom,
                DataPeriodTo = a.DataPeriodTo,
                ValidUntilUtc = a.ValidUntilUtc
            }).ToList();

            _db.TenancyMemberRoleScopes.AddRange(newScopes);
            await _db.SaveChangesAsync(cancellationToken);

            var roles = await _db.TenancyRoles.AsNoTracking().Where(r => r.TenantId == tenantId).ToDictionaryAsync(r => r.Id, cancellationToken);
            return Result.Success(MapMemberDto(membership, newScopes, roles));
        }, cancellationToken);

        if (!txResult.IsSuccess)
        {
            return txResult;
        }

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            actorUserId,
            "TenantAdmin",
            "TENANCY_MEMBER_ROLES_UPDATED",
            TargetTenantId: tenantId,
            ResourcePath: "/api/v1/settings/members",
            MetadataJson: JsonSerializer.Serialize(new { membershipId = txResult.Value.MembershipId, targetUserId })), cancellationToken);

        return txResult;
    }

    public async Task<Result> RevokeMemberAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid targetUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);

        var txResult = await ExecuteSerializedTenantTransactionAsync(tenantId, async () =>
        {
            var membership = await _db.TenancyMemberships
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == targetUserId && m.Status == "Active", cancellationToken);
            if (membership is null)
            {
                return Result.Failure<RevokedMemberInfo>(Error.NotFound("MEMBER_NOT_FOUND", "Active tenant member was not found."));
            }

            var ceilingCheck = await RoleScopeAssignmentValidator.EnsureActorCanManageTargetMemberAsync(
                _db, _permissionResolver, tenantId, actorUserId, membership.Id, _clock.UtcNow, null, cancellationToken);
            if (!ceilingCheck.IsSuccess)
            {
                return Result.Failure<RevokedMemberInfo>(ceilingCheck.Error);
            }

            var ownerRole = await _db.TenancyRoles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Code == RoleCodes.CompanyOwner, cancellationToken);
            if (ownerRole is not null)
            {
                var isOwner = await _db.TenancyMemberRoleScopes
                    .AnyAsync(s => s.TenantId == tenantId && s.MembershipId == membership.Id && s.RoleId == ownerRole.Id, cancellationToken);
                if (isOwner && !await OtherActiveOwnerExistsAsync(tenantId, membership.Id, ownerRole.Id, cancellationToken))
                {
                    return Result.Failure<RevokedMemberInfo>(Error.Conflict(
                        "LAST_OWNER_PROTECTED",
                        "Cannot revoke the last active, permanent, tenant-wide CompanyOwner in this tenant."));
                }
            }

            membership.Status = "Revoked";
            membership.RevokedAtUtc = _clock.UtcNow;
            membership.RevokeReason = string.IsNullOrWhiteSpace(reason) ? "MemberRevoked" : reason.Trim();
            await PendingInvitationRevoker.RevokePendingForUserAsync(_db, _identityAuth, tenantId, targetUserId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new RevokedMemberInfo(membership.Id, membership.RevokeReason));
        }, cancellationToken);

        if (!txResult.IsSuccess)
        {
            return Result.Failure(txResult.Error);
        }

        await _sessionRevoker.RevokeUserTenantSessionsAsync(tenantId, targetUserId, txResult.Value.Reason, cancellationToken);

        await _outbox.PublishRawAsync(
            "Tenancy.MemberRevoked",
            JsonSerializer.Serialize(new { tenantId, targetUserId, txResult.Value.MembershipId, RevokeReason = txResult.Value.Reason }),
            tenantId: tenantId,
            autoSave: true,
            cancellationToken: cancellationToken);

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            actorUserId,
            "TenantAdmin",
            "TENANCY_MEMBER_REVOKED",
            TargetTenantId: tenantId,
            ResourcePath: "/api/v1/settings/members",
            Reason: txResult.Value.Reason,
            MetadataJson: JsonSerializer.Serialize(new { membershipId = txResult.Value.MembershipId, targetUserId })), cancellationToken);

        return Result.Success();
    }

    private readonly record struct RevokedMemberInfo(Guid MembershipId, string Reason);

    private Task<Result<T>> ExecuteSerializedTenantTransactionAsync<T>(
        Guid tenantId,
        Func<Task<Result<T>>> action,
        CancellationToken cancellationToken) =>
        TenantTransactionSerializer.ExecuteAsync(_db, tenantId, action, cancellationToken);

    private async Task<bool> OtherActiveOwnerExistsAsync(Guid tenantId, Guid excludedMembershipId, Guid ownerRoleId, CancellationToken cancellationToken)
    {
        return await (
            from s in _db.TenancyMemberRoleScopes.AsNoTracking()
            join m in _db.TenancyMemberships.AsNoTracking() on s.MembershipId equals m.Id
            where s.TenantId == tenantId
               && s.RoleId == ownerRoleId
               && s.ScopeType == nameof(DataScope.Tenant)
               && s.ValidUntilUtc == null
               && m.TenantId == tenantId
               && m.Id != excludedMembershipId
               && m.Status == "Active"
               && m.RevokedAtUtc == null
            select s.Id).AnyAsync(cancellationToken);
    }

    private static TenantMemberDto MapMemberDto(
        TenancyMembership membership,
        IEnumerable<TenancyMemberRoleScope> scopes,
        IReadOnlyDictionary<Guid, TenancyRole> roles)
    {
        var roleDtos = scopes.Select(s =>
        {
            roles.TryGetValue(s.RoleId, out var role);
            var targets = string.IsNullOrWhiteSpace(s.ScopeTargetIdsJson)
                ? Array.Empty<Guid>()
                : (JsonSerializer.Deserialize<Guid[]>(s.ScopeTargetIdsJson) ?? Array.Empty<Guid>());
            return new MemberRoleScopeDto(
                s.Id,
                s.RoleId,
                role?.Code ?? "Unknown",
                role?.Name ?? "Unknown",
                s.ScopeType,
                targets,
                s.DataPeriodFrom,
                s.DataPeriodTo,
                s.ValidUntilUtc);
        }).ToList();

        return new TenantMemberDto(
            membership.Id,
            membership.UserId,
            membership.Status,
            membership.IsEmergencyAccount,
            membership.JoinedAtUtc,
            membership.RevokedAtUtc,
            roleDtos);
    }
}
