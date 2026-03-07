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
        private readonly ILogger<S3StorageService> _logger;

        public bool IsEnabled => true;

        public S3StorageService(IConfiguration configuration, ILogger<S3StorageService> logger)
        {
            _logger = logger;
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
                _logger.LogInformation("S3StorageService initialized with explicit AWS credentials");
            }
            else
            {
                // Fall back to default credential chain (IAM role, env vars, etc.)
                _s3Client = new AmazonS3Client(region);
                _logger.LogWarning("S3StorageService initialized with default credential chain (no explicit credentials found)");
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
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    _logger.LogWarning("GetPreSignedUrl called with empty key");
                    return null;
                }

                var request = new GetPreSignedUrlRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    Expires = DateTime.UtcNow.AddMinutes(expirationMinutes)
                };

                var url = _s3Client.GetPreSignedURL(request);
                _logger.LogInformation($"Generated pre-signed URL for key: {key}");
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating pre-signed URL for key: {key}");
                return null;
            }
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
