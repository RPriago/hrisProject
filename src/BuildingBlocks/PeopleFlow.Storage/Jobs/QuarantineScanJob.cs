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

        using var tenantScope = _tenantContextAccessor?.BeginScope(new TenantContext(envelope.TenantId, "Active"));

        var storedFile = await _dbContext.StoredFiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == envelope.TargetEntityId, cancellationToken);

        if (storedFile == null || storedFile.TenantId != envelope.TenantId)
        {
            throw new KeyNotFoundException($"StoredFile '{envelope.TargetEntityId}' not found for tenant '{envelope.TenantId}'.");
        }

        if (storedFile.ScanStatus != FileStatus.Quarantined)
        {
            return; // Already processed
        }

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
}
