namespace CEMS.Services
{
    public interface IGmailService
    {
        bool IsConfigured { get; }
        string GetAuthorizationUrl(string state);
        Task<GmailTokenResponse?> ExchangeCodeForToken(string code);
        Task<bool> RefreshAccessToken(string refreshToken);
        Task<string?> GetAccessToken(string refreshToken);
    }

    public class GmailTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public string? TokenType { get; set; }
    }
}
