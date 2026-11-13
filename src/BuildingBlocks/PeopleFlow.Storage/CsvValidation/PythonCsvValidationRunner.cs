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
        ArgumentNullException.ThrowIfNull(csvStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);

        if (_options.MaxInputCsvBytes <= 0)
        {
            throw new InvalidOperationException("MaxInputCsvBytes must be greater than zero.");
        }

        if (csvStream.CanSeek && csvStream.Length > _options.MaxInputCsvBytes)
        {
            throw new InvalidOperationException(
                $"CSV input size exceeds maximum allowed limit of {_options.MaxInputCsvBytes} bytes.");
        }

        var scriptPath = ResolveValidatorScriptPath();
        var tempDir = Path.Combine(Path.GetTempPath(), $"csv-val-{Guid.NewGuid():N}");
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(
                tempDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        else
        {
            Directory.CreateDirectory(tempDir);
        }

        try
        {
            var manifestPath = Path.Combine(tempDir, "manifest.json");
            var inputPath = Path.Combine(tempDir, "input.csv");
            var reportPath = Path.Combine(tempDir, "report.json");
            var ndjsonPath = Path.Combine(tempDir, "normalized.ndjson");

            await File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken);

            await using (var fileStream = File.Create(inputPath))
            {
                var buffer = new byte[81920];
                long totalWritten = 0;
                int bytesRead;
                while ((bytesRead = await csvStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalWritten += bytesRead;
                    if (totalWritten > _options.MaxInputCsvBytes)
                    {
                        throw new InvalidOperationException(
                            $"CSV input size exceeds maximum allowed limit of {_options.MaxInputCsvBytes} bytes.");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
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

            ConfigureSanitizedEnvironment(startInfo, tempDir);

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

    private static void ConfigureSanitizedEnvironment(ProcessStartInfo startInfo, string tempDir)
    {
        var preserved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] allowedKeys =
        [
            "PATH", "SYSTEMROOT", "WINDIR", "PATHEXT", "LANG", "LC_ALL"
        ];

        foreach (var key in allowedKeys)
        {
            var val = startInfo.Environment.TryGetValue(key, out var v) ? v : Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(val))
            {
                preserved[key] = val;
            }
        }

        startInfo.Environment.Clear();
        foreach (var kv in preserved)
        {
            startInfo.Environment[kv.Key] = kv.Value;
        }

        startInfo.Environment["TMPDIR"] = tempDir;
        startInfo.Environment["TMP"] = tempDir;
        startInfo.Environment["TEMP"] = tempDir;
        startInfo.Environment["PYTHONNOUSERSITE"] = "1";
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        startInfo.Environment["PYTHONUTF8"] = "1";
    }
}
