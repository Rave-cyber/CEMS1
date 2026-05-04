using System.Net.Http;
using System.Text.Json;

namespace CEMS.Services
{
    /// <summary>
    /// Sends SMS via Semaphore (https://semaphore.co) — popular PH SMS gateway.
    /// Set Semaphore:ApiKey and optionally Semaphore:SenderName in appsettings.
    /// </summary>
    public class SemaphoreSmsService : ISmsService
    {
        private readonly HttpClient _http;
        private readonly string? _apiKey;
        private readonly string _senderName;
        private readonly ILogger<SemaphoreSmsService> _logger;

        public SemaphoreSmsService(HttpClient http, IConfiguration config, ILogger<SemaphoreSmsService> logger)
        {
            _http = http;
            _apiKey = config["Semaphore:ApiKey"];
            _senderName = config["Semaphore:SenderName"] ?? "CEMS";
            _logger = logger;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<bool> SendAsync(string toNumber, string message)
        {
            if (!IsConfigured) return false;

            try
            {
                // Normalize PH number: strip spaces/dashes, ensure starts with 63
                var number = toNumber.Replace(" ", "").Replace("-", "").Replace("+", "");
                if (number.StartsWith("0")) number = "63" + number[1..];
                if (!number.StartsWith("63")) number = "63" + number;

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["apikey"]      = _apiKey!,
                    ["number"]      = number,
                    ["message"]     = message,
                    ["sendername"]  = _senderName
                });

                var response = await _http.PostAsync("https://api.semaphore.co/api/v4/messages", content);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SMS sent to {Number} via Semaphore.", number);
                    return true;
                }

                _logger.LogWarning("Semaphore SMS failed for {Number}: {Body}", number, body);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Semaphore SMS exception for {Number}.", toNumber);
                return false;
            }
        }
    }
}
