using System.Collections.Concurrent;
using PeopleFlow.Storage.Abstractions;

namespace PeopleFlow.Storage.Services;

/// <summary>
/// Thread-safe in-memory S3 storage double for hermetic unit and integration testing.
/// </summary>
public class InMemoryS3BlobStorage : IS3BlobStorage
{
    private readonly ConcurrentDictionary<(string Bucket, string Key), byte[]> _storage = new();

    public async Task PutObjectAsync(string bucketName, string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        _storage[(bucketName, key)] = ms.ToArray();
    }

    public Task PutObjectAsync(string bucketName, string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        _storage[(bucketName, key)] = (byte[])content.Clone();
        return Task.CompletedTask;
    }

    public Task CopyObjectAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue((sourceBucket, sourceKey), out var bytes))
        {
            _storage[(destinationBucket, destinationKey)] = (byte[])bytes.Clone();
        }
        return Task.CompletedTask;
    }

    public Task DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        _storage.TryRemove((bucketName, key), out _);
        return Task.CompletedTask;
    }

    public Task<Stream?> GetObjectStreamAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue((bucketName, key), out var bytes))
        {
            return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        }
        return Task.FromResult<Stream?>(null);
    }

    public string GeneratePresignedUrl(string bucketName, string key, TimeSpan expiry, string downloadFileName)
    {
        var sanitizedFileName = downloadFileName
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace("\"", "\\\"");

        var encodedName = Uri.EscapeDataString(sanitizedFileName);
        return $"https://storage.peopleflow.internal/{bucketName}/{key}?response-content-disposition=attachment%3B%20filename%3D%22{encodedName}%22&expires={DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds()}";
    }

    public bool Exists(string bucketName, string key) => _storage.ContainsKey((bucketName, key));

    public Task<bool> ExistsAsync(string bucketName, string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Exists(bucketName, key));
}
