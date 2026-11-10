using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Storage.Abstractions;

/// <summary>
/// Service interface for private multi-tenant file storage, quarantine state machine, and presigned downloads.
/// </summary>
public interface IPrivateFileService
{
    /// <summary>
    /// Stores uploaded file into the quarantine bucket and records StoredFile entry as Quarantined.
    /// </summary>
    Task<StoredFile> UploadToQuarantineAsync(
        Guid tenantId,
        string fileName,
        string contentType,
        Stream content,
        string ownerModule = "Default",
        string ownerEntityType = "Attachment",
        Guid? ownerEntityId = null,
        string sensitivityLevel = "Internal",
        string retentionCategory = "Default",
        Guid? uploadedByUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a verified file from quarantine to private bucket and updates StoredFile status to Available.
    /// </summary>
    Task<StoredFile> PromoteToAvailableAsync(
        Guid storedFileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects an infected file, deletes the quarantined payload, and updates status to RejectedInfected.
    /// </summary>
    Task<StoredFile> RejectInfectedAsync(
        Guid storedFileId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a file due to invalid or dangerous type / magic bytes, deletes the quarantined payload, and updates status to RejectedInvalidType.
    /// </summary>
    Task<StoredFile> RejectInvalidTypeAsync(
        Guid storedFileId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a strictly capped short-lived presigned download URL (max 120s) with Content-Disposition attachment.
    /// Rejects files that are not Available (e.g. Quarantined or RejectedInfected).
    /// </summary>
    Task<string> GeneratePresignedDownloadUrlAsync(
        Guid storedFileId,
        int expirySeconds = 120,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a strictly capped short-lived presigned download URL (max 120s) with Content-Disposition attachment.
    /// </summary>
    Task<string> GeneratePresignedDownloadUrlAsync(
        Guid storedFileId,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read stream for the stored file.
    /// </summary>
    Task<Stream> OpenReadStreamAsync(
        Guid storedFileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds a verified (Available) StoredFile to its domain owner entity.
    /// Prevents cross-tenant re-binding or binding unverified quarantined files.
    /// </summary>
    Task<StoredFile> BindToOwnerEntityAsync(
        Guid storedFileId,
        string ownerModule,
        string ownerEntityType,
        Guid ownerEntityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates current total storage consumed in bytes by a tenant across all non-deleted files.
    /// </summary>
    Task<long> GetTenantStorageUsageBytesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
