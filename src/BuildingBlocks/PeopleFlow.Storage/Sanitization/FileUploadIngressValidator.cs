using System.Text;

namespace PeopleFlow.Storage.Sanitization;

/// <summary>
/// Validates uploaded filenames, extensions, declared MIME types, and binary magic bytes
/// before persisting payloads to quarantine storage.
/// </summary>
public static class FileUploadIngressValidator
{
    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".sh", ".bat", ".cmd", ".dll", ".so", ".ps1", ".vbs", ".js", ".msi", ".com", ".pif", ".scr"
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedExtensionMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new(StringComparer.OrdinalIgnoreCase) { "application/pdf" },
        [".png"] = new(StringComparer.OrdinalIgnoreCase) { "image/png" },
        [".jpg"] = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg" },
        [".jpeg"] = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg" },
        [".webp"] = new(StringComparer.OrdinalIgnoreCase) { "image/webp" },
        [".xlsx"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/zip"
        },
        [".docx"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/zip"
        },
        [".csv"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "text/csv",
            "application/csv",
            "text/plain",
            "application/vnd.ms-excel"
        },
        [".txt"] = new(StringComparer.OrdinalIgnoreCase) { "text/plain" }
    };

    public static string SanitizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var segments = fileName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var leaf = segments.Length > 0 ? segments[^1] : string.Empty;

        var builder = new StringBuilder(leaf.Length);
        foreach (var ch in leaf)
        {
            if (char.IsControl(ch) || ch is '"' or ';' or ':' or '<' or '>' or '|' or '*' or '?')
            {
                continue;
            }

            builder.Append(ch);
        }

        var cleaned = builder.ToString().Trim().Trim('.');
        while (cleaned.Contains("..", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("..", string.Empty, StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new InvalidOperationException("Uploaded filename is invalid after sanitization.");
        }

        return cleaned;
    }

    public static void ValidateAndInspectHeader(
        string sanitizedFileName,
        string contentType,
        ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sanitizedFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (payload.Length == 0)
        {
            throw new InvalidOperationException("Uploaded file payload must not be empty.");
        }

        var fileSegments = sanitizedFileName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (fileSegments.Length < 2)
        {
            throw new InvalidOperationException("Uploaded filename must include a valid allowed extension.");
        }

        for (var i = 1; i < fileSegments.Length; i++)
        {
            var extSegment = "." + fileSegments[i];
            if (DangerousExtensions.Contains(extSegment))
            {
                throw new InvalidOperationException(
                    $"Uploaded file extension '{extSegment}' is blocked for security reasons.");
            }
        }

        var extension = "." + fileSegments[^1];
        if (!AllowedExtensionMimeTypes.TryGetValue(extension, out var allowedMimes))
        {
            throw new InvalidOperationException($"Uploaded file extension '{extension}' is not in the allowlist.");
        }

        var normalizedMime = contentType.Split(';', 2)[0].Trim();
        if (!allowedMimes.Contains(normalizedMime))
        {
            throw new InvalidOperationException(
                $"Content-Type '{normalizedMime}' does not match extension '{extension}'.");
        }

        if (HasExecutableMagicHeader(payload))
        {
            throw new InvalidOperationException(
                "Uploaded file contains executable magic bytes and is rejected at ingress.");
        }

        if (!MatchesExpectedMagicBytes(extension, payload))
        {
            throw new InvalidOperationException(
                $"Uploaded payload binary signature does not match declared '{extension}' file type.");
        }
    }

    public static bool HasExecutableMagicHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 2)
        {
            if (header[0] == 0x4D && header[1] == 0x5A) return true; // MZ (Win32 PE)
            if (header[0] == 0x23 && header[1] == 0x21) return true; // #! shebang
        }

        if (header.Length >= 4)
        {
            if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46) return true; // ELF
            if ((header[0] == 0xFE && header[1] == 0xED && header[2] == 0xFA && (header[3] == 0xCE || header[3] == 0xCF)) ||
                ((header[0] == 0xCE || header[0] == 0xCF) && header[1] == 0xFA && header[2] == 0xED && header[3] == 0xFE))
            {
                return true; // Mach-O
            }
        }

        return false;
    }

    private static bool MatchesExpectedMagicBytes(string extension, ReadOnlySpan<byte> payload)
    {
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => payload.Length >= 5 &&
                      payload[0] == 0x25 && payload[1] == 0x50 && payload[2] == 0x44 && payload[3] == 0x46 && payload[4] == 0x2D,
            ".png" => payload.Length >= 8 &&
                      payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4E && payload[3] == 0x47 &&
                      payload[4] == 0x0D && payload[5] == 0x0A && payload[6] == 0x1A && payload[7] == 0x0A,
            ".jpg" or ".jpeg" => payload.Length >= 3 &&
                      payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF,
            ".webp" => payload.Length >= 12 &&
                      payload[0] == 0x52 && payload[1] == 0x49 && payload[2] == 0x46 && payload[3] == 0x46 &&
                      payload[8] == 0x57 && payload[9] == 0x45 && payload[10] == 0x42 && payload[11] == 0x50,
            ".xlsx" or ".docx" => payload.Length >= 4 &&
                      payload[0] == 0x50 && payload[1] == 0x4B &&
                      ((payload[2] == 0x03 && payload[3] == 0x04) || (payload[2] == 0x05 && payload[3] == 0x06)),
            ".csv" or ".txt" => !payload.Contains((byte)0),
            _ => false
        };
    }
}
