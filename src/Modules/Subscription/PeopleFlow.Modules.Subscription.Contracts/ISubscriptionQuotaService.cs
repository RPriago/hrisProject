using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Modules.Subscription.Contracts;

public interface ISubscriptionQuotaService
{
    Task<Result> CheckEmployeeQuotaAsync(Guid tenantId, int currentActiveCount, CancellationToken cancellationToken = default);
    Task<Result> CheckStorageQuotaAsync(Guid tenantId, long currentUsedBytes, long additionalBytes, CancellationToken cancellationToken = default);
    Task<Result> CheckApiClientQuotaAsync(Guid tenantId, int currentActiveClientCount, CancellationToken cancellationToken = default);
}
