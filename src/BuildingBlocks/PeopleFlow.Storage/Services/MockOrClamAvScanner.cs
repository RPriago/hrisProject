using System.Text;
using PeopleFlow.Storage.Abstractions;

namespace PeopleFlow.Storage.Services;

/// <summary>
/// Scans files for EICAR test signature and dangerous executable extensions.
/// Acts as the local mock / embedded implementation of ClamAV scanning.
/// </summary>
public class MockOrClamAvScanner : IFileQuarantineScanner
{
    public const string EicarSignature = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
    private static readonly byte[] EicarBytes = Encoding.ASCII.GetBytes(EicarSignature);

    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".sh", ".bat", ".cmd", ".dll", ".so"
    };

    public Task<FileScanResult> ScanAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        return ScanAsync(ms, fileName, cancellationToken);
    }

    public async Task<FileScanResult> ScanAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        // 1. Validate file extensions across all segments to detect double extensions (e.g. document.exe.pdf)
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var segments = Path.GetFileName(fileName).Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i < segments.Length; i++)
            {
                var extCandidate = "." + segments[i];
                if (DangerousExtensions.Contains(extCandidate))
                {
                    return new FileScanResult(false, $"Dangerous executable extension: Heuristic.BlockedExtension.{extCandidate.TrimStart('.')}");
                }
            }
        }

        // 2. Read stream content
        long originalPosition = 0;
        if (fileStream.CanSeek)
        {
            originalPosition = fileStream.Position;
            fileStream.Position = 0;
        }

        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        if (fileStream.CanSeek)
        {
            fileStream.Position = originalPosition;
        }

        // 3. Inspect magic bytes header for disguised executables
        var disguisedThreat = DetectDisguisedExecutableHeader(bytes);
        if (disguisedThreat != null)
        {
            return new FileScanResult(false, disguisedThreat);
        }

        // 4. Scan content for EICAR signature
        if (ContainsSequence(bytes, EicarBytes))
        {
            return new FileScanResult(false, "EICAR-STANDARD-ANTIVIRUS-TEST-FILE");
        }

        return new FileScanResult(true);
    }

    private static string? DetectDisguisedExecutableHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 2)
        {
            return null;
        }

        // 1. Windows PE / DOS Executable ("MZ" = 0x4D, 0x5A)
        if (header[0] == 0x4D && header[1] == 0x5A)
        {
            return "Heuristic.DisguisedExecutable.Win32PE";
        }

        // 2. Shell script shebang ("#!" = 0x23, 0x21) disguised as document/image/csv
        if (header[0] == 0x23 && header[1] == 0x21)
        {
            return "Heuristic.DisguisedExecutable.ShellScript";
        }

        if (header.Length >= 4)
        {
            // 3. Linux ELF ("0x7F E L F")
            if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
            {
                return "Heuristic.DisguisedExecutable.ELF";
            }

            // 4. macOS Mach-O (FE ED FA CE / FE ED FA CF / CE FA ED FE / CF FA ED FE)
            var isMachO =
                (header[0] == 0xFE && header[1] == 0xED && header[2] == 0xFA && (header[3] == 0xCE || header[3] == 0xCF)) ||
                ((header[0] == 0xCE || header[0] == 0xCF) && header[1] == 0xFA && header[2] == 0xED && header[3] == 0xFE);
            if (isMachO)
            {
                return "Heuristic.DisguisedExecutable.MachO";
            }
        }

        return null;
    }

    private static bool ContainsSequence(byte[] source, byte[] pattern)
    {
        if (pattern.Length == 0 || source.Length < pattern.Length)
        {
            return false;
        }

        for (var i = 0; i <= source.Length - pattern.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }
}
