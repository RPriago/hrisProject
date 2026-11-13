using System.Security.Cryptography;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PeopleFlow.Kernel.Time;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Persistence.Audit;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;
using PeopleFlow.Storage.Abstractions;
using PeopleFlow.Storage.Common;
using PeopleFlow.Storage.Sanitization;

namespace PeopleFlow.Storage.Services;

/// <summary>
/// Implements private S3 file management, quarantine lifecycle, and presigned download URL generation.
/// Strictly caps presigned download URL expiry to a maximum of 120 seconds.
/// </summary>
public class S3PrivateFileService : IPrivateFileService
{
    private readonly PeopleFlowDbContext _dbContext;
    private readonly IS3BlobStorage _blobStorage;
    private readonly StorageOptions _options;
    private readonly ITenantContextAccessor? _tenantContextAccessor;
    private readonly ILogger<S3PrivateFileService> _logger;
    private readonly IClock _clock;
    private readonly IAuditLogWriter? _auditLogWriter;

    public S3PrivateFileService(
        PeopleFlowDbContext dbContext,
        IS3BlobStorage blobStorage,
        StorageOptions? options = null,
        ITenantContextAccessor? tenantContextAccessor = null,
        ILogger<S3PrivateFileService>? logger = null,
        IClock? clock = null,
        IAuditLogWriter? auditLogWriter = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _options = options ?? new StorageOptions();
        _tenantContextAccessor = tenantContextAccessor;
        _logger = logger ?? NullLogger<S3PrivateFileService>.Instance;
        _clock = clock ?? new SystemClock();
        _auditLogWriter = auditLogWriter;
    }

    public S3PrivateFileService(
        PeopleFlowDbContext dbContext,
        IAmazonS3 s3Client,
        StorageOptions? options = null,
        ITenantContextAccessor? tenantContextAccessor = null,
        ILogger<S3PrivateFileService>? logger = null,
        IClock? clock = null,
        IAuditLogWriter? auditLogWriter = null)
        : this(dbContext, new AwsS3BlobStorage(s3Client), options, tenantContextAccessor, logger, clock, auditLogWriter)
    {
    }

    public async Task<StoredFile> UploadToQuarantineAsync(
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
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        }

        if (_dbContext.CurrentTenantId.HasValue && _dbContext.CurrentTenantId.Value != tenantId)
        {
            throw new InvalidOperationException("Cannot upload file for a different tenant than the active ambient tenant.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);

        if (_options.MaxFileSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                "Storage:MaxFileSizeBytes must be configured to a positive limit before accepting file uploads.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var sanitizedFileName = FileUploadIngressValidator.SanitizeFileName(fileName);

        if (sanitizedFileName.Length > 255)
        {
            throw new ArgumentException("Sanitized file name exceeds maximum length of 255 characters.", nameof(fileName));
        }

        if (contentType.Length > 120)
        {
            throw new ArgumentException("Content-Type exceeds maximum length of 120 characters.", nameof(contentType));
        }

        using var tenantScope = _dbContext.BeginOperationTenantScope(tenantId);

        // Early check for seekable streams to avoid buffering oversized payloads in memory
        if (content.CanSeek && content.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File size exceeds maximum allowed limit of {_options.MaxFileSizeBytes} bytes.");
        }

        // Bounded read into memory to avoid uncontrolled memory amplification
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await content.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > _options.MaxFileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"File size exceeds maximum allowed limit of {_options.MaxFileSizeBytes} bytes.");
            }
            await ms.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        var payloadSpan = ms.GetBuffer().AsSpan(0, (int)ms.Length);
        FileUploadIngressValidator.ValidateAndInspectHeader(sanitizedFileName, contentType, payloadSpan);

        var checksum = Convert.ToHexString(SHA256.HashData(payloadSpan)).ToLowerInvariant();

        var fileId = Guid.CreateVersion7();
        var storageKey = $"quarantine/{tenantId}/{fileId}/{sanitizedFileName}";
        var bucket = _options.QuarantineBucket;

        ms.Position = 0;
        await _blobStorage.PutObjectAsync(bucket, storageKey, ms, contentType, cancellationToken);

        var storedFile = new StoredFile(fileId)
        {
            TenantId = tenantId,
            OwnerModule = ownerModule,
            OwnerEntityType = ownerEntityType,
            OwnerEntityId = ownerEntityId,
            SensitivityLevel = sensitivityLevel,
            OriginalFileName = sanitizedFileName,
            ContentType = contentType,
            SizeBytes = ms.Length,
            Sha256Checksum = checksum,
            StorageKey = storageKey,
            BucketName = bucket,
            ScanStatus = FileStatus.Quarantined,
            RetentionCategory = retentionCategory,
            UploadedByUserId = uploadedByUserId
        };

        _dbContext.StoredFiles.Add(storedFile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return storedFile;
    }

    public async Task<StoredFile> PromoteToAvailableAsync(
        Guid storedFileId,
        CancellationToken cancellationToken = default)
    {
        var (storedFile, tenantScope) = await GetRequiredTenantScopedFileAsync(storedFileId, cancellationToken);
        using var _ = tenantScope;

        // Idempotent retry: if already Available, return immediately without re-copying or deleting
        if (storedFile.ScanStatus == FileStatus.Available)
        {
            return storedFile;
        }

        if (storedFile.ScanStatus != FileStatus.Quarantined && storedFile.ScanStatus != FileStatus.Scanning)
        {
            throw new InvalidOperationException(
                $"Cannot promote file '{storedFileId}' with status '{storedFile.ScanStatus}'. File must be in '{FileStatus.Quarantined}' or '{FileStatus.Scanning}' status.");
        }

        var currentBucket = storedFile.BucketName ?? _options.QuarantineBucket;
        var destinationBucket = _options.PrivateBucket;
        var destinationKey = $"private/tenants/{storedFile.TenantId}/{storedFile.OwnerModule}/{storedFile.Id}/{FileUploadIngressValidator.SanitizeFileName(storedFile.OriginalFileName)}";

        // Copy from quarantine to private
        await _blobStorage.CopyObjectAsync(currentBucket, storedFile.StorageKey, destinationBucket, destinationKey, cancellationToken);

        // Delete from quarantine
        await _blobStorage.DeleteObjectAsync(currentBucket, storedFile.StorageKey, cancellationToken);

        // Update database record
        storedFile.BucketName = destinationBucket;
        storedFile.StorageKey = destinationKey;
        storedFile.ScanStatus = FileStatus.Available;
        storedFile.ScannedAtUtc = _clock.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return storedFile;
    }

    public async Task<StoredFile> RejectInfectedAsync(
        Guid storedFileId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var (storedFile, tenantScope) = await GetRequiredTenantScopedFileAsync(storedFileId, cancellationToken);
        using var _ = tenantScope;

        // Idempotent retry: if already rejected, return immediately
        if (storedFile.ScanStatus == FileStatus.RejectedInfected)
        {
            return storedFile;
        }

        if (storedFile.ScanStatus == FileStatus.Available)
        {
            throw new InvalidOperationException(
                $"Cannot reject file '{storedFileId}' because it was already promoted to '{FileStatus.Available}'.");
        }

        var currentBucket = storedFile.BucketName ?? _options.QuarantineBucket;

        // Delete infected payload from quarantine
        await _blobStorage.DeleteObjectAsync(currentBucket, storedFile.StorageKey, cancellationToken);

        // Update status in database
        storedFile.ScanStatus = FileStatus.RejectedInfected;
        storedFile.ScannedAtUtc = _clock.UtcNow;

        var auditWriter = _auditLogWriter ?? new OutboxAuditLogWriter(_dbContext);
        var auditEntry = new AuditLogEntry(
            TenantId: storedFile.TenantId,
            ActorUserId: storedFile.UploadedByUserId,
            SessionId: null,
            CorrelationId: Guid.CreateVersion7(),
            Module: "Security",
            Action: "MalwareFileRejected",
            EntityType: "StoredFile",
            EntityId: storedFile.Id.ToString(),
            OldValues: null,
            NewValues: new Dictionary<string, object?>
            {
                ["FileName"] = storedFile.OriginalFileName,
                ["Reason"] = reason ?? "Infected with malware",
                ["ScanStatus"] = FileStatus.RejectedInfected
            },
            Reason: reason ?? "Malware detected during quarantine scan");
        await auditWriter.WriteAsync(auditEntry, autoSave: false, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return storedFile;
    }

    public async Task<StoredFile> RejectInvalidTypeAsync(
        Guid storedFileId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var (storedFile, tenantScope) = await GetRequiredTenantScopedFileAsync(storedFileId, cancellationToken);
        using var _ = tenantScope;

        if (storedFile.ScanStatus == FileStatus.RejectedInvalidType)
        {
            return storedFile; // Idempotent
        }

        if (storedFile.ScanStatus == FileStatus.Available)
        {
            throw new InvalidOperationException(
                $"Cannot reject file '{storedFileId}' as invalid type after it has already been promoted to '{FileStatus.Available}'.");
        }

        _logger.LogWarning("StoredFile {StoredFileId} was rejected as invalid type. Reason: {Reason}", storedFileId, reason ?? "Unspecified");

        var currentBucket = storedFile.BucketName ?? _options.QuarantineBucket;

        // Delete invalid payload from quarantine
        await _blobStorage.DeleteObjectAsync(currentBucket, storedFile.StorageKey, cancellationToken);

        // Update status in database using entity domain method
        storedFile.MarkRejectedInvalidType(_clock.UtcNow);

        var auditWriter = _auditLogWriter ?? new OutboxAuditLogWriter(_dbContext);
        var auditEntry = new AuditLogEntry(
            TenantId: storedFile.TenantId,
            ActorUserId: storedFile.UploadedByUserId,
            SessionId: null,
            CorrelationId: Guid.CreateVersion7(),
            Module: "Security",
            Action: "InvalidFileTypeRejected",
            EntityType: "StoredFile",
            EntityId: storedFile.Id.ToString(),
            OldValues: null,
            NewValues: new Dictionary<string, object?>
            {
                ["FileName"] = storedFile.OriginalFileName,
                ["Reason"] = reason ?? "Invalid file type",
                ["ScanStatus"] = FileStatus.RejectedInvalidType
            },
            Reason: reason ?? "Invalid file type detected during quarantine scan");
        await auditWriter.WriteAsync(auditEntry, autoSave: false, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return storedFile;
    }

    public Task<string> GeneratePresignedDownloadUrlAsync(
        Guid storedFileId,
        int expirySeconds = 120,
        CancellationToken cancellationToken = default)
    {
        if (expirySeconds <= 0 || expirySeconds > _options.MaxPresignedExpirySeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expirySeconds),
                $"Presigned download URL expiry must be between 1 and {_options.MaxPresignedExpirySeconds} seconds. Requested: {expirySeconds}s.");
        }

        return GeneratePresignedDownloadUrlAsync(storedFileId, TimeSpan.FromSeconds(expirySeconds), cancellationToken);
    }

    public async Task<string> GeneratePresignedDownloadUrlAsync(
        Guid storedFileId,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromSeconds(_options.MaxPresignedExpirySeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiry),
                $"Presigned download URL expiry must be between 1 and {_options.MaxPresignedExpirySeconds} seconds. Requested: {expiry.TotalSeconds}s.");
        }

        var (storedFile, tenantScope) = await GetRequiredTenantScopedFileAsync(storedFileId, cancellationToken);
        using var _ = tenantScope;

        if (storedFile.ScanStatus != FileStatus.Available)
        {
            throw new InvalidOperationException(
                $"Cannot generate presigned download URL for file '{storedFileId}' with status '{storedFile.ScanStatus}'. File must be '{FileStatus.Available}'.");
        }

        var bucket = storedFile.BucketName ?? _options.PrivateBucket;
        var key = storedFile.StorageKey;
        var sanitizedFileName = FileUploadIngressValidator.SanitizeFileName(storedFile.OriginalFileName);

        if (!await _blobStorage.ExistsAsync(bucket, key, cancellationToken))
        {
            throw new FileNotFoundException($"StoredFile '{storedFileId}' object was not found in bucket '{bucket}'.");
        }

        return _blobStorage.GeneratePresignedUrl(bucket, key, expiry, sanitizedFileName);
    }

    public async Task<Stream> OpenReadStreamAsync(
        Guid storedFileId,
        CancellationToken cancellationToken = default)
    {
        var (storedFile, tenantScope) = await GetRequiredTenantScopedFileAsync(storedFileId, cancellationToken);
        using var _ = tenantScope;

        if (storedFile.ScanStatus != FileStatus.Available)
        {
            throw new InvalidOperationException(
                $"Cannot open stream for file '{storedFileId}' with status '{storedFile.ScanStatus}'. File must be '{FileStatus.Available}'.");
        }

        var bucket = storedFile.BucketName ?? _options.PrivateBucket;
        var stream = await _blobStorage.GetObjectStreamAsync(bucket, storedFile.StorageKey, cancellationToken);

        return stream ?? throw new FileNotFoundException($"Object stream for file '{storedFileId}' was not found in bucket '{bucket}'.");
    }

    public async Task<StoredFile> BindToOwnerEntityAsync(
        Guid storedFileId,
        string ownerModule,
        string ownerEntityType,
        Guid ownerEntityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModule);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerEntityType);
        if (ownerEntityId == Guid.Empty)
        {
            throw new ArgumentException("OwnerEntityId must not be empty.", nameof(ownerEntityId));
        }

        var (storedFile, tenantScope) = await GetRequiredTenantScopedFileAsync(storedFileId, cancellationToken);
        using var _ = tenantScope;

        if (storedFile.ScanStatus != FileStatus.Available)
        {
            throw new InvalidOperationException(
                $"Cannot bind file '{storedFileId}' with status '{storedFile.ScanStatus}'. File must be '{FileStatus.Available}'.");
        }

        if (storedFile.OwnerEntityId.HasValue &&
            (storedFile.OwnerEntityId.Value != ownerEntityId ||
             !string.Equals(storedFile.OwnerEntityType, ownerEntityType, StringComparison.Ordinal) ||
             !string.Equals(storedFile.OwnerModule, ownerModule, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"StoredFile '{storedFileId}' is already bound to '{storedFile.OwnerModule}/{storedFile.OwnerEntityType}/{storedFile.OwnerEntityId.Value}'.");
        }

        storedFile.OwnerModule = ownerModule;
        storedFile.OwnerEntityType = ownerEntityType;
        storedFile.OwnerEntityId = ownerEntityId;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return storedFile;
    }

    public async Task<long> GetTenantStorageUsageBytesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        }

        if (_dbContext.CurrentTenantId.HasValue && tenantId != _dbContext.CurrentTenantId.Value)
        {
            throw new UnauthorizedAccessException("Cannot inspect storage usage of another tenant.");
        }

        using var tenantScope = _dbContext.BeginOperationTenantScope(tenantId);

        var totalBytes = await _dbContext.StoredFiles
            .Where(f => f.TenantId == tenantId &&
                        f.ScanStatus != FileStatus.RejectedInfected &&
                        f.ScanStatus != FileStatus.RejectedMalware &&
                        f.ScanStatus != FileStatus.RejectedInvalidType &&
                        f.ScanStatus != FileStatus.DeletedByRetention)
            .SumAsync(f => (long?)f.SizeBytes, cancellationToken);

        return totalBytes ?? 0L;
    }

    private async Task<(StoredFile File, IDisposable TenantScope)> GetRequiredTenantScopedFileAsync(
        Guid storedFileId,
        CancellationToken cancellationToken)
    {
        var activeTenantId = _tenantContextAccessor?.TenantId ?? _dbContext.CurrentTenantId;
        if (!activeTenantId.HasValue || activeTenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Active tenant context is required for file access.");
        }

        var tenantScope = _dbContext.BeginOperationTenantScope(activeTenantId.Value);
        try
        {
            var storedFile = await _dbContext.StoredFiles
                .FirstOrDefaultAsync(f => f.Id == storedFileId, cancellationToken)
                ?? throw new KeyNotFoundException($"StoredFile '{storedFileId}' was not found.");

            if (storedFile.TenantId != activeTenantId.Value)
            {
                throw new KeyNotFoundException($"StoredFile '{storedFileId}' was not found.");
            }

            return (storedFile, tenantScope);
        }
        catch
        {
            tenantScope.Dispose();
            throw;
        }
    }
}
