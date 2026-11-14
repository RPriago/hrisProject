using System.Text.Json;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Persistence.Audit;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class TenantSwitcherService : ITenantSwitcherService
{
    private readonly ITenantMembershipReader _membershipReader;
    private readonly ISessionContextResolver _sessionResolver;
    private readonly IPlatformAuditLogWriter _auditWriter;

    public TenantSwitcherService(
        ITenantMembershipReader membershipReader,
        ISessionContextResolver sessionResolver,
        IPlatformAuditLogWriter auditWriter)
    {
        _membershipReader = membershipReader ?? throw new ArgumentNullException(nameof(membershipReader));
        _sessionResolver = sessionResolver ?? throw new ArgumentNullException(nameof(sessionResolver));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    }

    public async Task<Result<SwitchTenantResultDto>> SwitchActiveTenantAsync(
        Guid userId,
        Guid sessionId,
        Guid targetTenantId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionResolver.ResolveActiveSessionAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return Result.Failure<SwitchTenantResultDto>(new Error("SESSION_INVALID", "Current session is invalid or revoked."));
        }

        if (session.SupportGrantId.HasValue)
        {
            return Result.Failure<SwitchTenantResultDto>(new Error("SUPPORT_SESSION_LOCKED", "Exit the active support session before switching tenants."));
        }

        var membership = await _membershipReader.GetActiveMembershipAsync(targetTenantId, userId, cancellationToken);
        if (membership is null)
        {
            return Result.Failure<SwitchTenantResultDto>(new Error("FORBIDDEN", "User is not an active member of the target tenant."));
        }

        if (membership.TenantStatus.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<SwitchTenantResultDto>(new Error("TENANT_SUSPENDED", "Target tenant is suspended."));
        }

        if (membership.RequireMfaForAll && !session.IsMfaVerified)
        {
            return Result.Failure<SwitchTenantResultDto>(new Error("MFA_REQUIRED_FOR_TENANT", "Target tenant requires MFA verification on the active session."));
        }

        var updated = await _sessionResolver.UpdateActiveTenantAsync(sessionId, targetTenantId, cancellationToken);
        if (!updated.IsSuccess)
        {
            return Result.Failure<SwitchTenantResultDto>(updated.Error);
        }

        await _auditWriter.WriteAsync(new PlatformAuditEntry(
            userId,
            "TenantMember",
            "TENANT_SWITCHED",
            TargetTenantId: targetTenantId,
            ResourcePath: "/api/v1/auth/switch-active-tenant",
            MetadataJson: JsonSerializer.Serialize(new { sessionId, membership.Slug })), cancellationToken);

        return Result.Success(new SwitchTenantResultDto(targetTenantId, membership.Slug, membership.DisplayName));
    }
}
