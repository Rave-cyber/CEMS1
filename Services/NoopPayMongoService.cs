using System;
using System.Threading.Tasks;

namespace CEMS.Services
{
    // A simple fallback service used when PayMongo is not configured in production.
    public class NoopPayMongoService : IPayMongoService
    {
        public Task<(string SessionId, string CheckoutUrl)> CreateCheckoutSessionAsync(decimal amount, string description, int reportId, string successUrl, string cancelUrl, string? customerEmail = null, string? customerName = null)
        {
            throw new InvalidOperationException("PayMongo is not configured. Set PayMongo:SecretKey in configuration or configure the environment variable PayMongo__SecretKey.");
        }

        public Task<string> GetCheckoutStatusAsync(string sessionId)
        {
            throw new InvalidOperationException("PayMongo is not configured. Set PayMongo:SecretKey in configuration or configure the environment variable PayMongo__SecretKey.");
        }
    }
}
