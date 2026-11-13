using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using PeopleFlow.Storage.Abstractions;
using PeopleFlow.Storage.Common;
using PeopleFlow.Storage.Sanitization;

namespace PeopleFlow.Storage.Services;

/// <summary>
/// Scans quarantined files via a live ClamAV daemon (clamd) using the native zINSTREAM\0 TCP protocol,
/// failing closed whenever the daemon is unreachable or returns an invalid response.
/// </summary>
public sealed class ClamAvDaemonScanner : IFileQuarantineScanner
{
    private static readonly byte[] InstreamHeader = Encoding.ASCII.GetBytes("zINSTREAM\0");
    private static readonly byte[] EndOfStreamTerminator = [0, 0, 0, 0];
    private readonly StorageOptions _options;

    public ClamAvDaemonScanner(StorageOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<FileScanResult> ScanAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        return await ScanAsync(ms, fileName, cancellationToken);
    }

    public async Task<FileScanResult> ScanAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        if (!string.IsNullOrWhiteSpace(fileName) &&
            FileUploadIngressValidator.FindDangerousExtension(fileName) is { } blockedExtension)
        {
            return new FileScanResult(false, $"Heuristic.BlockedExtension:{blockedExtension}");
        }

        if (_options.MaxFileSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                "Storage:MaxFileSizeBytes must be configured to a positive limit before scanning files.");
        }

        long originalPosition = 0;
        if (fileStream.CanSeek)
        {
            originalPosition = fileStream.Position;
            fileStream.Position = 0;

            if (fileStream.Length > _options.MaxFileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"File size exceeds maximum allowed scan limit of {_options.MaxFileSizeBytes} bytes.");
            }
        }

        try
        {
            // Read only a small fixed header window to detect disguised executables before opening a network stream
            const int headerInspectSize = 64;
            var headerLimit = (int)Math.Min(headerInspectSize, _options.MaxFileSizeBytes);
            var headerBuffer = new byte[headerLimit];
            var headerLength = 0;

            while (headerLength < headerBuffer.Length)
            {
                var read = await fileStream.ReadAsync(
                    headerBuffer.AsMemory(headerLength, headerBuffer.Length - headerLength),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                headerLength += read;
            }

            if (FileUploadIngressValidator.HasExecutableMagicHeader(headerBuffer.AsSpan(0, headerLength)))
            {
                return new FileScanResult(false, "Heuristic.DisguisedExecutable");
            }

            if (string.IsNullOrWhiteSpace(_options.ClamAvHost) || _options.ClamAvPort <= 0)
            {
                throw new InvalidOperationException("Storage:ClamAvHost and Storage:ClamAvPort must be validly configured.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ClamAvTimeoutSeconds)));

            using var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(_options.ClamAvHost, _options.ClamAvPort, timeoutCts.Token);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"ClamAV daemon unreachable at {_options.ClamAvHost}:{_options.ClamAvPort}. Scan failed closed.",
                    ex);
            }

            await using var networkStream = tcpClient.GetStream();
            await networkStream.WriteAsync(InstreamHeader, timeoutCts.Token);

            const int chunkSize = 8192;
            var lenPrefix = new byte[4];
            long totalStreamed = headerLength;

            if (headerLength > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(lenPrefix, (uint)headerLength);
                await networkStream.WriteAsync(lenPrefix, timeoutCts.Token);
                await networkStream.WriteAsync(headerBuffer.AsMemory(0, headerLength), timeoutCts.Token);
            }

            var chunkBuffer = new byte[chunkSize];
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(chunkBuffer, timeoutCts.Token)) > 0)
            {
                totalStreamed += bytesRead;
                if (totalStreamed > _options.MaxFileSizeBytes)
                {
                    throw new InvalidOperationException(
                        $"File size exceeds maximum allowed scan limit of {_options.MaxFileSizeBytes} bytes.");
                }

                BinaryPrimitives.WriteUInt32BigEndian(lenPrefix, (uint)bytesRead);
                await networkStream.WriteAsync(lenPrefix, timeoutCts.Token);
                await networkStream.WriteAsync(chunkBuffer.AsMemory(0, bytesRead), timeoutCts.Token);
            }

            await networkStream.WriteAsync(EndOfStreamTerminator, timeoutCts.Token);
            await networkStream.FlushAsync(timeoutCts.Token);

            using var reader = new StreamReader(networkStream, Encoding.ASCII);
            var response = (await reader.ReadToEndAsync(timeoutCts.Token)).Trim('\0', '\r', '\n', ' ');

            if (response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return new FileScanResult(true);
            }

            if (response.EndsWith("FOUND", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = response.IndexOf(':');
                var withoutPrefix = colonIndex >= 0 ? response[(colonIndex + 1)..].Trim() : response;
                var threatName = withoutPrefix[..^"FOUND".Length].Trim();
                return new FileScanResult(false, string.IsNullOrWhiteSpace(threatName) ? "ClamAV.ThreatDetected" : threatName);
            }

            throw new InvalidOperationException($"ClamAV daemon returned unexpected response '{response}'. Scan failed closed.");
        }
        finally
        {
            if (fileStream.CanSeek)
            {
                fileStream.Position = originalPosition;
            }
        }
    }
}
