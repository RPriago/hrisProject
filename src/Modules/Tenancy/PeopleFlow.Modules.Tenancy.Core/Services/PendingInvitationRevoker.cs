using Microsoft.EntityFrameworkCore;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

internal static class PendingInvitationRevoker
{
    public static async Task RevokePendingForUserAsync(
        TenancyDbContext db,
        IIdentityAuthService? identityAuth,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (identityAuth is null)
        {
            return;
        }

        var profile = await identityAuth.GetUserProfileAsync(userId, cancellationToken);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Email))
        {
            return;
        }

        var normalizedEmail = profile.Email.Trim().ToLowerInvariant();
        var pending = await db.TenancyInvitations
            .Where(i => i.TenantId == tenantId && i.InvitedEmail == normalizedEmail && i.Status == "Pending")
            .ToListAsync(cancellationToken);

        foreach (var invite in pending)
        {
            invite.Status = "Revoked";
        }
    }
}
