using System.ComponentModel.DataAnnotations.Schema;
using PeopleFlow.Kernel.Domain;

namespace PeopleFlow.Persistence.Entities;

/// <summary>
/// Infrastructure record for tracking mutation idempotency and preventing duplicate processing.
/// Maps to table 'infra_idempotency_records'.
/// </summary>
public class IdempotencyRecord : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ActorUserId { get; set; }
    public string EndpointKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string Status { get; set; } = "InProgress";
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBodyJson { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(24);

    // Convenience aliases
    [NotMapped]
    public string Key
    {
        get => IdempotencyKey;
        set => IdempotencyKey = value;
    }

    [NotMapped]
    public string? ResponseBody
    {
        get => ResponseBodyJson;
        set => ResponseBodyJson = value;
    }

    public IdempotencyRecord() : base()
    {
    }

    public IdempotencyRecord(Guid id) : base(id)
    {
    }
}
