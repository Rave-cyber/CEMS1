namespace CEMS.Services
{
    public interface IS3StorageService
    {
        
        bool IsEnabled { get; }
       
        Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);

        string? GetPreSignedUrl(string key, int expirationMinutes = 15);

        /// <summary>Delete a file from S3.</summary>
        Task DeleteFileAsync(string key);
    }
}
