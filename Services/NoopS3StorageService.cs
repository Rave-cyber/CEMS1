namespace CEMS.Services
{
    
    public class NoopS3StorageService : IS3StorageService
    {
        public bool IsEnabled => false;

        public Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
        {
            throw new InvalidOperationException(
                "AWS S3 is not configured. Set AWS:BucketName, AWS:AccessKey and AWS:SecretKey in configuration.");
        }

        public string? GetPreSignedUrl(string key, int expirationMinutes = 15)
        {
            return null;
        }

        public Task DeleteFileAsync(string key)
        {
            throw new InvalidOperationException(
                "AWS S3 is not configured. Set AWS:BucketName, AWS:AccessKey and AWS:SecretKey in configuration.");
        }
    }
}
