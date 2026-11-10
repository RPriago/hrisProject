namespace PeopleFlow.Storage.Common;

public sealed class StorageOptions
{
    public string QuarantineBucket { get; set; } = "peopleflow-quarantine";
    public string PrivateBucket { get; set; } = "peopleflow-private";
    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024; // 25 MB default
    public TimeSpan DefaultPresignedExpiry { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxPresignedExpirySeconds { get; set; } = 120;
    public bool UseS3 { get; set; } = false;
    public string? EndpointUrl { get; set; }
    public string? ServiceUrl { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Region { get; set; } = "ap-southeast-1";
    public bool ForcePathStyle { get; set; } = true;
}
