using System.Text.Json.Serialization;

namespace PeopleFlow.Storage.CsvValidation;

public class CsvRowError
{
    [JsonPropertyName("row_index")]
    public int RowIndex { get; set; }

    [JsonPropertyName("column")]
    public string Column { get; set; } = string.Empty;

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class CsvValidationReport
{
    [JsonPropertyName("total_rows")]
    public int TotalRows { get; set; }

    [JsonPropertyName("valid_rows")]
    public int ValidRows { get; set; }

    [JsonPropertyName("error_rows")]
    public int ErrorRows { get; set; }

    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("errors")]
    public List<CsvRowError> Errors { get; set; } = new();

    [JsonPropertyName("errors_truncated")]
    public bool ErrorsTruncated { get; set; }
}

public class CsvValidationResult
{
    public CsvValidationReport Report { get; }
    public IReadOnlyList<string> NormalizedRowsJson { get; }

    public CsvValidationResult(CsvValidationReport report, IReadOnlyList<string> normalizedRowsJson)
    {
        Report = report;
        NormalizedRowsJson = normalizedRowsJson;
    }

    public Stream OpenNormalizedStream()
    {
        var content = string.Join("\n", NormalizedRowsJson);
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
    }
}

public class PythonCsvValidationOptions
{
    public string PythonExecutablePath { get; set; } = "python3";
    public string? ValidatorScriptPath { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public long MaxInputCsvBytes { get; set; } = 25 * 1024 * 1024; // 25 MB
}

public interface IPythonCsvValidationRunner
{
    Task<CsvValidationResult> ValidateAsync(Stream csvStream, string manifestJson, CancellationToken cancellationToken = default);
    Task<CsvValidationResult> ValidateAsync(string csvContent, string manifestJson, CancellationToken cancellationToken = default);
}
