using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Common;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Identity.Contracts;
using PeopleFlow.Modules.Identity.Core.Persistence;

namespace PeopleFlow.Modules.Identity.Core.Services;

public sealed class SessionService : IUserSessionRevoker, ISessionContextResolver
{
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;

    public SessionService(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ResolvedSessionInfo?> ResolveActiveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var joined = await (
            from sess in _dbContext.IdentityUserSessions
            join usr in _dbContext.IdentityUsers.AsNoTracking() on sess.UserId equals usr.Id
            where sess.Id == sessionId
            select new { sess, usr })
            .SingleOrDefaultAsync(cancellationToken);

        if (joined == null ||
            joined.sess.RevokedAtUtc.HasValue ||
            joined.sess.ExpiresAtUtc <= now ||
            !joined.usr.IsActive)
        {
            return null;
        }

        if ((now - joined.sess.LastSeenAtUtc) > TimeSpan.FromMinutes(1))
        {
            joined.sess.LastSeenAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ResolvedSessionInfo(
            joined.sess.Id,
            joined.usr.Id,
            joined.usr.Email,
            joined.usr.IsActive,
            joined.usr.IsPlatformAdmin,
            joined.usr.IsBreakGlass,
            joined.usr.TwoFactorEnabled,
            joined.sess.ActiveTenantId,
            joined.sess.SupportGrantId,
            joined.sess.MfaVerifiedAtUtc.HasValue,
            joined.sess.ExpiresAtUtc);
    }

    public async Task<Result> UpdateActiveTenantAsync(Guid sessionId, Guid? activeTenantId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.IdentityUserSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session == null || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= _clock.UtcNow)
        {
            return Result.Failure(Error.Unauthorized("SESSION_INVALID", "Active session not found."));
        }

        session.ActiveTenantId = activeTenantId;
        session.SupportGrantId = null;
        session.LastSeenAtUtc = _clock.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetSupportGrantAsync(
        Guid sessionId,
        Guid? supportGrantId,
        Guid? targetTenantId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.IdentityUserSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session == null || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= _clock.UtcNow)
        {
            return Result.Failure(Error.Unauthorized("SESSION_INVALID", "Active session not found."));
        }

        session.SupportGrantId = supportGrantId;
        session.ActiveTenantId = targetTenantId;
        session.LastSeenAtUtc = _clock.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<int> ClearSupportGrantSessionsAsync(Guid supportGrantId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var sessions = await _dbContext.IdentityUserSessions
            .Where(s => s.SupportGrantId == supportGrantId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var s in sessions)
        {
            s.SupportGrantId = null;
            s.ActiveTenantId = null;
            s.LastSeenAtUtc = now;
        }

        if (sessions.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return sessions.Count;
    }

    public async Task<int> RevokeUserTenantSessionsAsync(
        Guid tenantId,
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var sessions = await _dbContext.IdentityUserSessions
            .Where(s => s.UserId == userId && s.ActiveTenantId == tenantId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var s in sessions)
        {
            s.RevokedAtUtc = now;
            s.RevokeReason = reason;
        }

        if (sessions.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return sessions.Count;
    }

    public async Task<int> RevokeAllUserSessionsAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var sessions = await _dbContext.IdentityUserSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && (!exceptSessionId.HasValue || s.Id != exceptSessionId.Value))
            .ToListAsync(cancellationToken);

        foreach (var s in sessions)
        {
            s.RevokedAtUtc = now;
            s.RevokeReason = reason;
        }

        if (sessions.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return sessions.Count;
    }
}
