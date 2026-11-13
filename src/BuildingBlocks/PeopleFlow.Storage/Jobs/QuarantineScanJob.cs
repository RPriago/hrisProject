using Microsoft.EntityFrameworkCore;
using PeopleFlow.Jobs.Envelopes;
using PeopleFlow.MultiTenancy.Abstractions;
using PeopleFlow.MultiTenancy.Context;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;
using PeopleFlow.Storage.Abstractions;
using PeopleFlow.Storage.Common;

namespace PeopleFlow.Storage.Jobs;

/// <summary>
/// Background job that scans uploaded quarantined files for malware, EICAR signatures, and blocked file types.
/// Operates under zero-data payload constraints by only accepting a JobEnvelope.
/// </summary>
public class QuarantineScanJob
{
    private readonly IPrivateFileService _fileService;
    private readonly IFileQuarantineScanner _scanner;
    private readonly IS3BlobStorage _blobStorage;
    private readonly PeopleFlowDbContext _dbContext;
    private readonly StorageOptions _options;
    private readonly ITenantContextAccessor? _tenantContextAccessor;

    public QuarantineScanJob(
        IPrivateFileService fileService,
        IFileQuarantineScanner scanner,
        IS3BlobStorage blobStorage,
        PeopleFlowDbContext dbContext,
        StorageOptions options,
        ITenantContextAccessor? tenantContextAccessor = null)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tenantContextAccessor = tenantContextAccessor;
    }

    public virtual async Task ExecuteAsync(JobEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var tenantScope = _dbContext.BeginOperationTenantScope(envelope.TenantId);

        var storedFile = await _dbContext.StoredFiles
            .FirstOrDefaultAsync(f => f.Id == envelope.TargetEntityId, cancellationToken);

        if (storedFile == null || storedFile.TenantId != envelope.TenantId)
        {
            throw new KeyNotFoundException($"StoredFile '{envelope.TargetEntityId}' not found for tenant '{envelope.TenantId}'.");
        }

        if (storedFile.ScanStatus != FileStatus.Quarantined)
        {
            return; // Already processed or currently scanning
        }

        if (_dbContext.Database.IsRelational())
        {
            var claimedCount = await _dbContext.StoredFiles
                .Where(f => f.Id == storedFile.Id &&
                            f.TenantId == envelope.TenantId &&
                            f.ScanStatus == FileStatus.Quarantined)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(f => f.ScanStatus, FileStatus.Scanning),
                    cancellationToken);

            if (claimedCount == 0)
            {
                return; // Concurrent worker already claimed or processed this file
            }

            // Reload entity so provider concurrency tokens (e.g., PostgreSQL xmin) match the updated row
            await _dbContext.Entry(storedFile).ReloadAsync(cancellationToken);
        }
        else
        {
            storedFile.ScanStatus = FileStatus.Scanning;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var bucket = storedFile.BucketName ?? _options.QuarantineBucket;
            await using var stream = await _blobStorage.GetObjectStreamAsync(bucket, storedFile.StorageKey, cancellationToken);
            if (stream == null)
            {
                throw new FileNotFoundException($"Quarantine file object not found for '{storedFile.Id}'.");
            }

            var scanResult = await _scanner.ScanAsync(stream, storedFile.OriginalFileName, cancellationToken);
            if (scanResult.IsClean)
            {
                await _fileService.PromoteToAvailableAsync(storedFile.Id, cancellationToken);
            }
            else if (scanResult.ThreatName != null &&
                     (scanResult.ThreatName.Contains("BlockedExtension", StringComparison.OrdinalIgnoreCase) ||
                      scanResult.ThreatName.Contains("DisguisedExecutable", StringComparison.OrdinalIgnoreCase)))
            {
                await _fileService.RejectInvalidTypeAsync(storedFile.Id, scanResult.ThreatName, cancellationToken);
            }
            else
            {
                await _fileService.RejectInfectedAsync(storedFile.Id, scanResult.ThreatName, cancellationToken);
            }
        }
        catch
        {
            // Revert Scanning -> Quarantined on transient scan/blob error so background retries can re-claim the file
            if (_dbContext.Database.IsRelational())
            {
                await _dbContext.StoredFiles
                    .Where(f => f.Id == storedFile.Id &&
                                f.TenantId == envelope.TenantId &&
                                f.ScanStatus == FileStatus.Scanning)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(f => f.ScanStatus, FileStatus.Quarantined),
                        CancellationToken.None);
            }
            else
            {
                storedFile.ScanStatus = FileStatus.Quarantined;
                await _dbContext.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
    }
}
