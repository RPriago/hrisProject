using Microsoft.EntityFrameworkCore;
using PeopleFlow.Kernel.Time;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Persistence;

namespace PeopleFlow.Modules.Subscription.Core.Services;

public sealed class TenantSubscriptionQueryService : ITenantSubscriptionQueryService
{
    private readonly SubscriptionDbContext _dbContext;
    private readonly IClock _clock;

    public TenantSubscriptionQueryService(SubscriptionDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<TenantSubscriptionSnapshot?> GetSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var joined = await (
            from sub in _dbContext.TenantSubscriptions.AsNoTracking()
            join plan in _dbContext.SubscriptionPlans.AsNoTracking() on sub.PlanId equals plan.Id
            where sub.TenantId == tenantId
            select new { sub, plan })
            .SingleOrDefaultAsync(cancellationToken);

        return joined == null
            ? null
            : SubscriptionLifecycleService.MapSnapshot(joined.sub, joined.plan, _clock.UtcNow);
    }
}
