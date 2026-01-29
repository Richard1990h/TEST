using Stripe; // ✅ ADD THIS
using Stripe.Checkout;
using Microsoft.Extensions.Configuration;

namespace LittleHelperAI.Backend.Services
{
    public class StripeService
    {
        private readonly IConfiguration _config;

        public StripeService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<string> CreateCheckoutSessionAsync(string priceId, string mode, string successUrl, string cancelUrl)
        {
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                Mode = mode,
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return session.Url;
        }
    }
}
