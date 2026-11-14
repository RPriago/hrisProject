using PeopleFlow.Modules.Subscription.Contracts;

namespace PeopleFlow.Modules.Subscription.Core.Services;

internal sealed class DefaultActiveApiClientCounter : IActiveApiClientCounter
{
    public Task<int> CountActiveApiClientsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
