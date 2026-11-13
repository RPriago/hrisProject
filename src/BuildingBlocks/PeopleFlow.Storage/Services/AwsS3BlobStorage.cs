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
        return new OwnedS3Stream(response);
    }

    public string GeneratePresignedUrl(string bucketName, string key, TimeSpan expiry, string downloadFileName)
    {
        var leafName = FileUploadIngressValidatorSafeLeaf(downloadFileName);
        var asciiSafe = BuildAsciiFallbackFileName(leafName);
        var encodedUtf8 = Uri.EscapeDataString(leafName);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename=\"{asciiSafe}\"; filename*=UTF-8''{encodedUtf8}"
            }
        };

        return _s3Client.GetPreSignedURL(request);
    }

    private static string FileUploadIngressValidatorSafeLeaf(string fileName)
    {
        try
        {
            return PeopleFlow.Storage.Sanitization.FileUploadIngressValidator.SanitizeFileName(fileName);
        }
        catch
        {
            return "download";
        }
    }

    private static string BuildAsciiFallbackFileName(string fileName)
    {
        var sb = new System.Text.StringBuilder(fileName.Length);
        foreach (var ch in fileName)
        {
            if (ch is >= ' ' and <= '~' && ch is not ('"' or '\\' or ';'))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }
        return sb.Length > 0 ? sb.ToString() : "download";
    }

    private sealed class OwnedS3Stream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
        private int _disposed;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && disposing)
            {
                _inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await _inner.DisposeAsync();
                response.Dispose();
            }
            await base.DisposeAsync();
        }
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
