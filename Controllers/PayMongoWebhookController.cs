using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CEMS.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CEMS.Controllers
{
    [Route("webhook")]
    [ApiController]
    public class PayMongoWebhookController : ControllerBase
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly PayMongoOptions _options;

        public PayMongoWebhookController(Data.ApplicationDbContext db, IOptions<PayMongoOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        [HttpPost("paymongo")]
        public async Task<IActionResult> HandleWebhook()
        {
            var body = await new StreamReader(Request.Body).ReadToEndAsync();

            // Verify signature if webhook secret is configured
            if (!string.IsNullOrEmpty(_options.WebhookSecret))
            {
                var signature = Request.Headers["Paymongo-Signature"].FirstOrDefault();
                if (!VerifySignature(body, signature))
                {
                    return Unauthorized("Invalid signature");
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var data = root.GetProperty("data");
                var attributes = data.GetProperty("attributes");
                var eventType = attributes.GetProperty("type").GetString();

                if (eventType == "link.payment.paid")
                {
                    var paymentData = attributes.GetProperty("data");
                    var paymentAttributes = paymentData.GetProperty("attributes");

                    // Try to get the link_id from the payment source
                    string? linkId = null;
                    if (paymentAttributes.TryGetProperty("metadata", out var metadata) &&
                        metadata.TryGetProperty("link_id", out var linkIdProp))
                    {
                        linkId = linkIdProp.GetString();
                    }

                    if (!string.IsNullOrEmpty(linkId))
                    {
                        var payment = await _db.ReimbursementPayments
                            .Include(p => p.Report)
                                .ThenInclude(r => r!.Items)
                            .FirstOrDefaultAsync(p => p.PayMongoLinkId == linkId);

                        if (payment != null)
                        {
                            payment.Status = "paid";
                            payment.PaidAt = DateTime.UtcNow;

                            if (payment.Report != null)
                            {
                                payment.Report.Reimbursed = true;

                                foreach (var item in payment.Report.Items)
                                {
                                    var category = item.Category?.Trim() ?? "";
                                    var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                                    if (budget != null)
                                        budget.Spent += item.Amount;
                                }
                            }

                            await _db.SaveChangesAsync();
                        }
                    }
                }

                return Ok(new { status = "received" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Webhook error: {ex.Message}");
                return Ok(new { status = "error", message = ex.Message });
            }
        }

        private bool VerifySignature(string body, string? signatureHeader)
        {
            if (string.IsNullOrEmpty(signatureHeader))
                return false;

            try
            {
                // PayMongo signature format: t=timestamp,te=test_signature,li=live_signature
                var parts = signatureHeader.Split(',')
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => p[1]);

                if (!parts.TryGetValue("t", out var timestamp))
                    return false;

                var signedPayload = $"{timestamp}.{body}";
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
                var computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

                // Check against test or live signature
                var expectedSignature = parts.GetValueOrDefault("te") ?? parts.GetValueOrDefault("li") ?? "";

                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedSignature),
                    Encoding.UTF8.GetBytes(expectedSignature));
            }
            catch
            {
                return false;
            }
        }
    }
}
