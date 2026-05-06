using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CEMS.Services
{
    public class GmailService : IGmailService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<GmailService> _logger;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;

        public bool IsConfigured => true;

        public GmailService(IConfiguration configuration, HttpClient httpClient, ILogger<GmailService> logger)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _logger = logger;
            _clientId = _configuration["Gmail:ClientId"] ?? "";
            _clientSecret = _configuration["Gmail:ClientSecret"] ?? "";
            _redirectUri = _configuration["Gmail:RedirectUri"] ?? "";
        }

        public string GetAuthorizationUrl(string state)
        {
            const string googleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
            // Include gmail.send so the stored token can send OTP emails for password reset
            var scope = "https://www.googleapis.com/auth/userinfo.email " +
                        "https://www.googleapis.com/auth/gmail.send";

            return $"{googleAuthUrl}?" +
                   $"client_id={Uri.EscapeDataString(_clientId)}&" +
                   $"redirect_uri={Uri.EscapeDataString(_redirectUri)}&" +
                   $"response_type=code&" +
                   $"scope={Uri.EscapeDataString(scope)}&" +
                   $"access_type=offline&" +
                   $"prompt=consent&" +
                   $"state={Uri.EscapeDataString(state)}";
        }

        public async Task<GmailTokenResponse?> ExchangeCodeForToken(string code)
        {
            try
            {
                const string tokenUrl = "https://oauth2.googleapis.com/token";

                var requestData = new Dictionary<string, string>
                {
                    { "code", code },
                    { "client_id", _clientId },
                    { "client_secret", _clientSecret },
                    { "redirect_uri", _redirectUri },
                    { "grant_type", "authorization_code" }
                };

                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(tokenUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    _logger.LogError("ExchangeCodeForToken failed: {Status} {Body}", response.StatusCode, err);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Token exchange response: {Body}", jsonResponse);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var tokenResponse = JsonSerializer.Deserialize<GmailTokenResponse>(jsonResponse, options);

                // Do NOT overwrite RefreshToken with AccessToken — if Google didn't return a
                // refresh token, the user needs to reconnect with prompt=consent
                if (tokenResponse != null && string.IsNullOrEmpty(tokenResponse.RefreshToken))
                {
                    _logger.LogWarning("Google did not return a refresh token. " +
                        "User may need to disconnect and reconnect Gmail with prompt=consent.");
                }

                return tokenResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExchangeCodeForToken exception");
                return null;
            }
        }

        public async Task<bool> RefreshAccessToken(string refreshToken)
        {
            try
            {
                const string tokenUrl = "https://oauth2.googleapis.com/token";

                var requestData = new Dictionary<string, string>
                {
                    { "refresh_token", refreshToken },
                    { "client_id", _clientId },
                    { "client_secret", _clientSecret },
                    { "grant_type", "refresh_token" }
                };

                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(tokenUrl, content);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> GetUserEmailAsync(string accessToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accessToken))
                    return null;

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogError("GetUserEmailAsync failed: {Status} {Body}", response.StatusCode, body);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);

                return document.RootElement.TryGetProperty("email", out var emailElement)
                    ? emailElement.GetString()
                    : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUserEmailAsync exception");
                return null;
            }
        }

        public async Task<string?> GetAccessToken(string refreshToken)
        {
            try
            {
                const string tokenUrl = "https://oauth2.googleapis.com/token";

                var requestData = new Dictionary<string, string>
                {
                    { "refresh_token", refreshToken },
                    { "client_id", _clientId },
                    { "client_secret", _clientSecret },
                    { "grant_type", "refresh_token" }
                };

                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(tokenUrl, content);

                if (!response.IsSuccessStatusCode)
                    return null;

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var tokenData = JsonSerializer.Deserialize<GmailTokenResponse>(jsonResponse, options);

                return tokenData?.AccessToken;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sends an email via the Gmail API using the user's stored token.
        /// Tries the token as an access token first; if that fails, tries refreshing it.
        /// </summary>
        public async Task<bool> SendEmailAsync(string refreshToken, string toEmail, string subject, string htmlBody)
        {
            try
            {
                if (string.IsNullOrEmpty(refreshToken))
                {
                    _logger.LogError("SendEmailAsync: refreshToken is null/empty");
                    return false;
                }

                string accessToken;

                // If the stored value is a refresh token (starts with "1//"), exchange it
                // Otherwise try it directly as an access token
                if (refreshToken.StartsWith("1//") || refreshToken.Length < 200)
                {
                    var fresh = await GetAccessToken(refreshToken);
                    if (string.IsNullOrEmpty(fresh))
                    {
                        _logger.LogError("SendEmailAsync: GetAccessToken returned null. RefreshToken may be expired or lack gmail.send scope. Token prefix: {Prefix}",
                            refreshToken.Length > 10 ? refreshToken[..10] : refreshToken);
                        return false;
                    }
                    accessToken = fresh;
                }
                else
                {
                    // Stored as access token directly — try it
                    accessToken = refreshToken;
                }

                return (await SendEmailWithErrorAsync(accessToken, toEmail, subject, htmlBody)).Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendEmailAsync exception");
                return false;
            }
        }

        public async Task<(bool Success, string Error)> SendEmailWithErrorAsync(string refreshToken, string toEmail, string subject, string htmlBody)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return (false, "No token stored. Please reconnect Gmail from your profile.");

            string accessToken;
            if (refreshToken.StartsWith("1//") || refreshToken.Length < 200)
            {
                var fresh = await GetAccessToken(refreshToken);
                if (string.IsNullOrEmpty(fresh))
                    return (false, "Token expired or missing gmail.send scope. Please disconnect and reconnect Gmail from your profile.");
                accessToken = fresh;
            }
            else
            {
                accessToken = refreshToken;
            }

            var rawMessage =
                $"To: {toEmail}\r\n" +
                $"Subject: {subject}\r\n" +
                $"Content-Type: text/html; charset=utf-8\r\n\r\n" +
                htmlBody;

            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawMessage))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            var payload = JsonSerializer.Serialize(new { raw = encoded });
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode) return (true, "");

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gmail send failed {Status}: {Body}", response.StatusCode, body);
            return (false, $"Gmail API error {(int)response.StatusCode}: {body}");
        }
    }
}
