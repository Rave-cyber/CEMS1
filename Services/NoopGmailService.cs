using System;

namespace CEMS.Services
{
    // Fallback when Gmail OAuth is not configured in the target environment.
    public class NoopGmailService : IGmailService
    {
        public bool IsConfigured => false;

        public string GetAuthorizationUrl(string state)
        {
            throw new InvalidOperationException("Gmail integration is not configured. Set Gmail__ClientId, Gmail__ClientSecret, and Gmail__RedirectUri in production.");
        }

        public Task<GmailTokenResponse?> ExchangeCodeForToken(string code)
        {
            throw new InvalidOperationException("Gmail integration is not configured. Set Gmail__ClientId, Gmail__ClientSecret, and Gmail__RedirectUri in production.");
        }

        public Task<bool> RefreshAccessToken(string refreshToken)
        {
            throw new InvalidOperationException("Gmail integration is not configured. Set Gmail__ClientId, Gmail__ClientSecret, and Gmail__RedirectUri in production.");
        }

        public Task<string?> GetUserEmailAsync(string accessToken)
        {
            throw new InvalidOperationException("Gmail integration is not configured. Set Gmail__ClientId, Gmail__ClientSecret, and Gmail__RedirectUri in production.");
        }

        public Task<string?> GetAccessToken(string refreshToken)
        {
            throw new InvalidOperationException("Gmail integration is not configured. Set Gmail__ClientId, Gmail__ClientSecret, and Gmail__RedirectUri in production.");
        }

        public Task<bool> SendEmailAsync(string refreshToken, string toEmail, string subject, string htmlBody)
        {
            return Task.FromResult(false);
        }

        public Task<(bool Success, string Error)> SendEmailWithErrorAsync(string refreshToken, string toEmail, string subject, string htmlBody)
            => Task.FromResult<(bool, string)>((false, "Gmail not configured."));
    }
}
