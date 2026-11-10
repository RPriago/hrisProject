namespace PeopleFlow.Jobs.Filters;

/// <summary>
/// Validates tenant subscription state before allowing background job execution.
/// </summary>
public interface ITenantSubscriptionValidator
{
    bool IsTenantExecutionAllowed(Guid tenantId);
    string? GetSubscriptionStatus(Guid tenantId);
}
