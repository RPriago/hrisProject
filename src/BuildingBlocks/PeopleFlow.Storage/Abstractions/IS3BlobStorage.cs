namespace PeopleFlow.Storage.Abstractions;

/// <summary>
/// Abstraction for low-level S3 object operations supporting AWS S3, MinIO, and hermetic in-memory test doubles.
/// </summary>
public interface IS3BlobStorage
{
    Task PutObjectAsync(string bucketName, string key, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task CopyObjectAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken cancellationToken = default);
    Task DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default);
    Task<Stream?> GetObjectStreamAsync(string bucketName, string key, CancellationToken cancellationToken = default);
    string GeneratePresignedUrl(string bucketName, string key, TimeSpan expiry, string downloadFileName);
    bool Exists(string bucketName, string key);
    Task<bool> ExistsAsync(string bucketName, string key, CancellationToken cancellationToken = default);
}
