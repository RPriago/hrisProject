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

    public Task<FileScanResult> ScanAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        return ScanAsync(ms, fileName, cancellationToken);
    }

    public async Task<FileScanResult> ScanAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        long originalPosition = 0;
        if (fileStream.CanSeek)
        {
            originalPosition = fileStream.Position;
            fileStream.Position = 0;
        }

        try
        {
            using var headBuffer = new MemoryStream();
            await fileStream.CopyToAsync(headBuffer, cancellationToken);
            var payload = headBuffer.ToArray();

            if (FileUploadIngressValidator.HasExecutableMagicHeader(payload))
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
            var offset = 0;
            while (offset < payload.Length)
            {
                var count = Math.Min(chunkSize, payload.Length - offset);
                BinaryPrimitives.WriteUInt32BigEndian(lenPrefix, (uint)count);
                await networkStream.WriteAsync(lenPrefix, timeoutCts.Token);
                await networkStream.WriteAsync(payload.AsMemory(offset, count), timeoutCts.Token);
                offset += count;
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
