using Amazon.S3;
using Amazon.S3.Model;
using PeopleFlow.Storage.Abstractions;

namespace PeopleFlow.Storage.Services;

/// <summary>
/// S3 blob storage implementation leveraging AWS S3 SDK for MinIO and AWS S3 object stores.
/// </summary>
public class AwsS3BlobStorage : IS3BlobStorage
{
    private readonly IAmazonS3 _s3Client;

    public AwsS3BlobStorage(IAmazonS3 s3Client)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
    }

    public async Task PutObjectAsync(string bucketName, string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);
    }

    public async Task CopyObjectAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken cancellationToken = default)
    {
        var request = new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,
            DestinationBucket = destinationBucket,
            DestinationKey = destinationKey
        };

        await _s3Client.CopyObjectAsync(request, cancellationToken);
    }

    public async Task DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = key
        };

        await _s3Client.DeleteObjectAsync(request, cancellationToken);
    }

    public async Task<Stream?> GetObjectStreamAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var response = await _s3Client.GetObjectAsync(bucketName, key, cancellationToken);
        return response.ResponseStream;
    }

    public string GeneratePresignedUrl(string bucketName, string key, TimeSpan expiry, string downloadFileName)
    {
        var sanitizedFileName = downloadFileName
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace("\"", "\\\"");

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename=\"{sanitizedFileName}\""
            }
        };

        return _s3Client.GetPreSignedURL(request);
    }

    public bool Exists(string bucketName, string key)
    {
        try
        {
            _s3Client.GetObjectMetadataAsync(bucketName, key).GetAwaiter().GetResult();
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = key
            };
            await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
