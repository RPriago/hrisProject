namespace PeopleFlow.Jobs.Filters;

/// <summary>
/// Marks a Hangfire job method or class as a cross-tenant platform system sweep/retry job.
/// <see cref="TenantJobActivationFilter"/> validates that no unverified primitive arguments are passed
/// and explicitly clears ambient tenant context before invocation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PlatformSystemJobAttribute : Attribute
{
}

public sealed class PlatformJobEnvelope
{
    public PlatformJobEnvelope() { }

    public PlatformJobEnvelope(Guid targetRecordId, Guid correlationId)
    {
        if (targetRecordId == Guid.Empty) throw new ArgumentException("TargetRecordId cannot be empty.", nameof(targetRecordId));
        if (correlationId == Guid.Empty) throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));

        TargetRecordId = targetRecordId;
        CorrelationId = correlationId;
    }

    public Guid TargetRecordId { get; init; }
    public Guid CorrelationId { get; init; }
}
