using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace CEMS.Services
{
    public class S3StorageService : IS3StorageService, IDisposable
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public bool IsEnabled => true;

        public S3StorageService(IConfiguration configuration)
        {
            var section = configuration.GetSection("AWS");
            _bucketName = section["BucketName"]
                ?? throw new InvalidOperationException("AWS:BucketName is not configured.");

            var region = RegionEndpoint.GetBySystemName(
                section["Region"] ?? "ap-southeast-1");

            var accessKey = section["AccessKey"];
            var secretKey = section["SecretKey"];

            if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
            {
                var credentials = new BasicAWSCredentials(accessKey, secretKey);
                _s3Client = new AmazonS3Client(credentials, region);
            }
            else
            {
                // Fall back to default credential chain (IAM role, env vars, etc.)
                _s3Client = new AmazonS3Client(region);
            }
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
        {
           
            var key = $"receipts/{DateTime.UtcNow:yyyy/MM/dd}/{fileName}";

            var request = new TransferUtilityUploadRequest
            {
                InputStream = fileStream,
                Key = key,
                BucketName = _bucketName,
                ContentType = contentType
            };

            using var transfer = new TransferUtility(_s3Client);
            await transfer.UploadAsync(request);

            return key;
        }

        public string? GetPreSignedUrl(string key, int expirationMinutes = 15)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Expires = DateTime.UtcNow.AddMinutes(expirationMinutes)
            };

            return _s3Client.GetPreSignedURL(request);
        }

        public async Task DeleteFileAsync(string key)
        {
            await _s3Client.DeleteObjectAsync(_bucketName, key);
        }

        public void Dispose()
        {
            _s3Client?.Dispose();
        }
    }
}
