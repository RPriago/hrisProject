using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PeopleFlow.Jobs.Filters;
using PeopleFlow.Modules.Subscription.Contracts;
using PeopleFlow.Modules.Subscription.Core.Persistence;

namespace PeopleFlow.Modules.Subscription.Core.Services;

/// <summary>
/// Real database-backed validator replacing Dev/FailClosed stubs in PeopleFlow.Worker.
/// Fails closed if subscription row is missing, Suspended, Cancelled, or Read-Only PastDue.
/// </summary>
public sealed class JobSubscriptionValidator : ITenantSubscriptionValidator
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionStatusConstants.Active,
        SubscriptionStatusConstants.Trial
    };

    private readonly IServiceScopeFactory _scopeFactory;

    public JobSubscriptionValidator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public bool IsTenantExecutionAllowed(Guid tenantId)
    {
        var status = GetSubscriptionStatus(tenantId);
        return !string.IsNullOrWhiteSpace(status) && AllowedStatuses.Contains(status);
    }

    public string? GetSubscriptionStatus(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            return SubscriptionStatusConstants.Suspended;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SubscriptionDbContext>();
            var status = db.TenantSubscriptions.AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .Select(s => s.Status)
                .SingleOrDefault();

            return string.IsNullOrWhiteSpace(status)
                ? SubscriptionStatusConstants.Suspended
                : status;
        }
        catch
        {
            // Fail closed if database is unreachable or uninitialized
            return SubscriptionStatusConstants.Suspended;
        }
    }
}
