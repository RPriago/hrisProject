namespace PeopleFlow.Modules.Subscription.Core;

public sealed class BillingOptions
{
    public int MaxTimestampSkewSeconds { get; set; } = 300;
    public bool EnableSandboxSimulator { get; set; } = false;
    public Dictionary<string, string> WebhookSecrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
