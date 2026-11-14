using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Tenancy.Core.Persistence;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

public sealed class ActiveApiClientCounter : IActiveApiClientCounter
{
    private readonly TenancyDbContext _db;
    private readonly IClock _clock;

    public ActiveApiClientCounter(TenancyDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<int> CountActiveApiClientsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = _db.BeginOperationTenantScope(tenantId);
        var now = _clock.UtcNow;

        var expires = await _db.ApiIntegrationClients
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.RevokedAtUtc == null)
            .Select(c => c.ExpiresAtUtc)
            .ToListAsync(cancellationToken);

        return expires.Count(exp => exp > now);
    }
}
