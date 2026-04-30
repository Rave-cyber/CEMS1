using System.Text;
using System.Text.Json;

namespace CEMS.Services
{
    public class GmailService : IGmailService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;

        public GmailService(IConfiguration configuration, HttpClient httpClient)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _clientId = _configuration["Gmail:ClientId"] ?? "";
            _clientSecret = _configuration["Gmail:ClientSecret"] ?? "";
            _redirectUri = _configuration["Gmail:RedirectUri"] ?? "";
        }

        public string GetAuthorizationUrl(string state)
        {
            const string googleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
            var scope = "https://www.googleapis.com/auth/userinfo.email";

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
                    return null;

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var tokenResponse = JsonSerializer.Deserialize<GmailTokenResponse>(jsonResponse, options);

                if (tokenResponse != null && string.IsNullOrEmpty(tokenResponse.RefreshToken))
                {
                    tokenResponse.RefreshToken = tokenResponse.AccessToken;
                }

                return tokenResponse;
            }
            catch
            {
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
    }
}
