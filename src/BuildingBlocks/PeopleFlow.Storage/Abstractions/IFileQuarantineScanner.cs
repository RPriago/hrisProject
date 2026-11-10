namespace PeopleFlow.Storage.Abstractions;

public record FileScanResult(bool IsClean, string? ThreatName = null);

/// <summary>
/// Scans uploaded file streams or byte payloads for malware, EICAR signatures, and dangerous extensions.
/// </summary>
public interface IFileQuarantineScanner
{
    Task<FileScanResult> ScanAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    Task<FileScanResult> ScanAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default);
}
