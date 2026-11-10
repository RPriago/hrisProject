namespace PeopleFlow.MultiTenancy.Context;

/// <summary>
/// Tenant context accessor providing ambient tenant access and convenient scope activation.
/// </summary>
public sealed class TenantContextAccessor : AsyncLocalTenantContextAccessor
{
}
