namespace PeopleFlow.Security.Redaction;

/// <summary>
/// Marks a property, field, or parameter as containing Personally Identifiable Information (PII) or secrets
/// that must be redacted during logging, tracing, and serializing.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class SensitiveDataAttribute : Attribute
{
    public string Mask { get; }

    public SensitiveDataAttribute(string mask = "****")
    {
        Mask = mask;
    }
}
