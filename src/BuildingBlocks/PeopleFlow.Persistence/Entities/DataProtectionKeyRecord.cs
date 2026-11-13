namespace PeopleFlow.Persistence.Entities;

/// <summary>
/// Persists ASP.NET Core Data Protection key rings in PostgreSQL so multi-instance
/// Web and Worker hosts share session/cookie/anti-forgery encryption keys.
/// Global infrastructure table (no TenantId).
/// </summary>
public sealed class DataProtectionKeyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string? FriendlyName { get; set; }
    public string Xml { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
