namespace CEMS.Services
{
    public interface IGmailService
    {
        bool IsConfigured { get; }
        string GetAuthorizationUrl(string state);
        Task<GmailTokenResponse?> ExchangeCodeForToken(string code);
        Task<bool> RefreshAccessToken(string refreshToken);
        Task<string?> GetAccessToken(string refreshToken);
        Task<bool> SendEmailAsync(string refreshToken, string toEmail, string subject, string htmlBody);
        Task<(bool Success, string Error)> SendEmailWithErrorAsync(string refreshToken, string toEmail, string subject, string htmlBody);
    }

    public class GmailTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public string? TokenType { get; set; }
    }
}
