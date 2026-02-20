using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CEMS.Services
{
    public interface IPayMongoService
    {
        /// <summary>
        /// Creates a PayMongo Checkout Session (supports GCash, card, etc.).
        /// Returns (checkoutSessionId, checkoutUrl).
        /// </summary>
        Task<(string SessionId, string CheckoutUrl)> CreateCheckoutSessionAsync(
            decimal amount, string description, int reportId, string successUrl, string cancelUrl,
            string? customerEmail = null, string? customerName = null);

        /// <summary>
        /// Retrieves a checkout session by ID and returns payment status.
        /// </summary>
        Task<string> GetCheckoutStatusAsync(string sessionId);
    }

    public class PayMongoService : IPayMongoService
    {
        private readonly HttpClient _http;
        private readonly ILogger<PayMongoService> _logger;

        public PayMongoService(HttpClient http, ILogger<PayMongoService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<(string SessionId, string CheckoutUrl)> CreateCheckoutSessionAsync(
            decimal amount, string description, int reportId, string successUrl, string cancelUrl,
            string? customerEmail = null, string? customerName = null)
        {
            // PayMongo amounts are in centavos — use long to avoid Int32 overflow
            var amountInCentavos = (long)(amount * 100m);

            if (amountInCentavos < 100)
                throw new InvalidOperationException($"Amount must be at least ₱1.00 (got ₱{amount:N2}).");

            var attributes = new System.Text.Json.Nodes.JsonObject
            {
                ["description"] = description,
                ["payment_method_types"] = new System.Text.Json.Nodes.JsonArray("gcash"),
                ["line_items"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["currency"] = "PHP",
                        ["amount"] = amountInCentavos,
                        ["description"] = description,
                        ["name"] = $"Reimbursement - Report #{reportId}",
                        ["quantity"] = 1
                    }
                ),
                ["success_url"] = successUrl,
                ["cancel_url"] = cancelUrl
            };

            if (!string.IsNullOrEmpty(customerEmail))
            {
                attributes["customer_email"] = customerEmail;
                attributes["billing"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["email"] = customerEmail,
                    ["name"] = customerName ?? customerEmail
                };
            }

            var payload = new System.Text.Json.Nodes.JsonObject
            {
                ["data"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["attributes"] = attributes
                }
            };

            var json = payload.ToJsonString();
            _logger.LogInformation("PayMongo checkout request: {Json}", json);

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _http.PostAsync("checkout_sessions", content);
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("PayMongo checkout response ({Status}): {Body}", response.StatusCode, body);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PayMongo error ({response.StatusCode}): {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");
            var sessionId = data.GetProperty("id").GetString()!;
            var checkoutUrl = data.GetProperty("attributes").GetProperty("checkout_url").GetString()!;

            return (sessionId, checkoutUrl);
        }

        public async Task<string> GetCheckoutStatusAsync(string sessionId)
        {
            var response = await _http.GetAsync($"checkout_sessions/{sessionId}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PayMongo error ({response.StatusCode}): {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var attributes = doc.RootElement
                .GetProperty("data")
                .GetProperty("attributes");

            // Check if payments array has entries — if so, it's paid
            if (attributes.TryGetProperty("payments", out var payments) && payments.GetArrayLength() > 0)
            {
                return "paid";
            }

            var status = attributes.TryGetProperty("status", out var st) ? st.GetString() : "unknown";
            return status ?? "unknown";
        }
    }
}
