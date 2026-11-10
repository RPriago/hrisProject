using System.ComponentModel.DataAnnotations.Schema;
using PeopleFlow.Kernel.Domain;

namespace PeopleFlow.Persistence.Entities;

/// <summary>
/// Infrastructure record for tracking private quarantined object files in S3/MinIO.
/// Maps to table 'infra_stored_files'.
/// </summary>
public class StoredFile : BaseEntity
{
    public Guid TenantId { get; set; }
    public string OwnerModule { get; set; } = string.Empty;
    public string OwnerEntityType { get; set; } = string.Empty;
    public Guid? OwnerEntityId { get; set; }
    public string SensitivityLevel { get; set; } = "Internal";
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Sha256Checksum { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? BucketName { get; set; }
    public string ScanStatus { get; set; } = "Quarantined";
    public DateTimeOffset? ScannedAtUtc { get; set; }
    public string RetentionCategory { get; set; } = "Default";
    public Guid? UploadedByUserId { get; set; }

    // Convenience aliases
    [NotMapped]
    public long FileSizeBytes
    {
        get => SizeBytes;
        set => SizeBytes = value;
    }

    [NotMapped]
    public string ObjectKey
    {
        get => StorageKey;
        set => StorageKey = value;
    }

    [NotMapped]
    public string Status
    {
        get => ScanStatus;
        set => ScanStatus = value;
    }

    public StoredFile() : base()
    {
    }

    public StoredFile(Guid id) : base(id)
    {
    }

    public StoredFile(
        Guid id,
        Guid tenantId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string sha256Checksum,
        string scanStatus,
        string storageKey) : base(id)
    {
        TenantId = tenantId;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256Checksum = sha256Checksum;
        ScanStatus = scanStatus;
        StorageKey = storageKey;
        OwnerModule = "Core";
        OwnerEntityType = "Document";
        SensitivityLevel = "Internal";
        RetentionCategory = "Default";
    }

    public void MarkAvailable(string bucketName, string storageKey, DateTimeOffset scannedAtUtc)
    {
        BucketName = bucketName;
        StorageKey = storageKey;
        ScanStatus = "Available";
        ScannedAtUtc = scannedAtUtc;
    }

    public void MarkRejectedInvalidType(DateTimeOffset scannedAtUtc)
    {
        ScanStatus = "RejectedInvalidType";
        ScannedAtUtc = scannedAtUtc;
        UpdatedAtUtc = scannedAtUtc;
    }

    public void MarkDeletedByRetention(DateTimeOffset deletedAtUtc)
    {
        ScanStatus = "DeletedByRetention";
        UpdatedAtUtc = deletedAtUtc;
    }
}
