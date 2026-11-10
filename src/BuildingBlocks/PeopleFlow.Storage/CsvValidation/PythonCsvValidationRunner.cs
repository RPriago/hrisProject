using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PeopleFlow.Storage.CsvValidation;

public class PythonCsvValidationRunner : IPythonCsvValidationRunner
{
    private readonly PythonCsvValidationOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public PythonCsvValidationRunner(PythonCsvValidationOptions? options = null)
    {
        _options = options ?? new PythonCsvValidationOptions();
    }

    public async Task<CsvValidationResult> ValidateAsync(string csvContent, string manifestJson, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(Utf8NoBom.GetBytes(csvContent));
        return await ValidateAsync(stream, manifestJson, cancellationToken);
    }

    public async Task<CsvValidationResult> ValidateAsync(Stream csvStream, string manifestJson, CancellationToken cancellationToken = default)
    {
        var scriptPath = ResolveValidatorScriptPath();
        var tempDir = Path.Combine(Path.GetTempPath(), $"csv-val-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var manifestPath = Path.Combine(tempDir, "manifest.json");
            var inputPath = Path.Combine(tempDir, "input.csv");
            var reportPath = Path.Combine(tempDir, "report.json");
            var ndjsonPath = Path.Combine(tempDir, "normalized.ndjson");

            await File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken);

            await using (var fileStream = File.Create(inputPath))
            {
                await csvStream.CopyToAsync(fileStream, cancellationToken);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.Timeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.PythonExecutablePath,
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("--manifest");
            startInfo.ArgumentList.Add(manifestPath);
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("--out-ndjson");
            startInfo.ArgumentList.Add(ndjsonPath);
            startInfo.ArgumentList.Add("--out-report");
            startInfo.ArgumentList.Add(reportPath);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore process kill errors on cleanup
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new TimeoutException($"Python CSV validator execution timed out after {_options.Timeout.TotalSeconds}s.");
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            var stderr = await stderrTask;

            if (!File.Exists(reportPath))
            {
                throw new InvalidOperationException($"Validator failed to produce report (Exit code {process.ExitCode}): {stderr}");
            }

            var reportJson = await File.ReadAllTextAsync(reportPath, cancellationToken);
            var report = JsonSerializer.Deserialize<CsvValidationReport>(reportJson, JsonOptions)
                         ?? throw new InvalidOperationException("Failed to deserialize CSV validation report.");

            var normalizedRows = new List<string>();
            if (File.Exists(ndjsonPath))
            {
                using var reader = new StreamReader(ndjsonPath, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        normalizedRows.Add(line);
                    }
                }
            }

            return new CsvValidationResult(report, normalizedRows);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best effort temp dir cleanup
            }
        }
    }

    private string ResolveValidatorScriptPath()
    {
        if (!string.IsNullOrEmpty(_options.ValidatorScriptPath) && File.Exists(_options.ValidatorScriptPath))
        {
            return Path.GetFullPath(_options.ValidatorScriptPath);
        }

        var envPath = Environment.GetEnvironmentVariable("PEOPLEFLOW_CSV_VALIDATOR_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        // Check relative to working directory and base directory walking upwards
        var candidates = new List<string>();

        var currentDir = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(currentDir, "tools", "csv-validator-py", "validator.py"));

        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "tools", "csv-validator-py", "validator.py"));

        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            candidates.Add(Path.Combine(dir.FullName, "tools", "csv-validator-py", "validator.py"));
            dir = dir.Parent;
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException("Python CSV validator script 'validator.py' could not be found.");
    }
}
