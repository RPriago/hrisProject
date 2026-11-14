namespace PeopleFlow.Kernel.Domain;

/// <summary>
/// Marker interface for global/platform entities exempt from tenant-scoped RLS policies,
/// ambient tenant query filters, and active TenantId mutation guards.
/// </summary>
public interface IPlatformOwnedEntity
{
}
