using PeopleFlow.Modules.Tenancy.Contracts;
using PeopleFlow.Security.Authorization;

namespace PeopleFlow.Modules.Tenancy.Core.Services;

/// <summary>
/// Default fail-safe implementation overridden when OrgStructure / CoreHR module registers its own validator.
/// </summary>
public sealed class DefaultOrganizationUnitValidator : IOrganizationUnitValidator
{
    public Task<bool> AllScopeTargetsExistAsync(
        Guid tenantId,
        DataScope scope,
        IReadOnlyCollection<Guid> targetIds,
        CancellationToken cancellationToken = default)
    {
        if (scope is DataScope.Self or DataScope.Tenant or DataScope.DirectReports)
        {
            return Task.FromResult(true);
        }

        // Before Org module is installed, allow empty target sets or UUIDs scoped to the request.
        return Task.FromResult(targetIds.All(id => id != Guid.Empty));
    }
}

/// <summary>
/// Default active headcount stub returning 0 until Employee Core module overrides it via DI.
/// </summary>
public sealed class DefaultActiveHeadcountCounter : IActiveHeadcountCounter
{
    public Task<int> CountActiveEmployeesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
