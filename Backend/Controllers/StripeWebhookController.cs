using Stripe;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Data; // ✅ CORRECT
using LittleHelperAI.Backend.Services;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StripeWebhookController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<StripeWebhookController> _logger;
        private readonly IPurchaseRewardService _purchaseRewardService;
        private readonly ICreditSecurityService _creditSecurityService;

        public StripeWebhookController(
            ApplicationDbContext db, 
            IConfiguration config, 
            ILogger<StripeWebhookController> logger,
            IPurchaseRewardService purchaseRewardService,
            ICreditSecurityService creditSecurityService)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _purchaseRewardService = purchaseRewardService;
            _creditSecurityService = creditSecurityService;
        }

        [HttpPost]
        public async Task<IActionResult> HandleStripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            Event stripeEvent;

            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    _config["Stripe:WebhookSecret"] // ⚡ You must configure this from Stripe dashboard
                );
            }
            catch (StripeException e)
            {
                _logger.LogError($"Stripe webhook error: {e.Message}");
                return BadRequest();
            }

            // Process only payment success events
            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Session;

                if (session != null)
                {
                    await HandleCheckoutSessionCompleted(session);
                }
            }

            return Ok();
        }

        private async Task HandleCheckoutSessionCompleted(Session session)
        {
            try
            {
                var customerEmail = session.CustomerDetails.Email;
                if (string.IsNullOrEmpty(customerEmail))
                {
                    _logger.LogWarning("Stripe webhook: No customer email found.");
                    return;
                }

                var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == customerEmail);
                if (user == null)
                {
                    _logger.LogWarning($"Stripe webhook: No user found for email {customerEmail}");
                    return;
                }

                // Find the purchased plan based on price_id
                var priceId = session.LineItems?.FirstOrDefault()?.Price.Id 
                    ?? session.Metadata?["priceId"]; // fallback if metadata is needed

                if (string.IsNullOrEmpty(priceId))
                {
                    _logger.LogWarning("Stripe webhook: No PriceId found in session.");
                    return;
                }

                var plan = await _db.StripePlans.FirstOrDefaultAsync(p => p.PriceId == priceId);
                if (plan == null)
                {
                    _logger.LogWarning($"Stripe webhook: No plan found for PriceId {priceId}");
                    return;
                }

                // Get IP and User Agent for security audit
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers["User-Agent"].ToString();

                // ✅ Create AUDITED credit operation (3-level security)
                await _creditSecurityService.CreateAuditedCreditOperation(
                    userId: user.Id,
                    operationType: "PURCHASE",
                    creditsAmount: plan.Credits,
                    sourceType: "STRIPE_PURCHASE",
                    sourceReferenceId: $"plan:{plan.Id}:session:{session.Id}",
                    relatedUserId: null,
                    ipAddress: ipAddress,
                    userAgent: userAgent);

                // ✅ Save purchase history
                var userPlan = new UserPlan
                {
                    UserId = user.Id,
                    PlanId = plan.Id,
                    PurchasedAt = DateTime.UtcNow,
                    CreditsAdded = plan.Credits
                };

                _db.UserPlans.Add(userPlan);
                await _db.SaveChangesAsync();

                _logger.LogInformation($"✅ Stripe webhook: Added {plan.Credits} credits to user {user.Email}");

                // ✅ Process purchase-based referral rewards
                await _purchaseRewardService.ProcessPurchaseRewards(
                    purchaserId: user.Id, 
                    planId: plan.Id,
                    ipAddress: ipAddress,
                    userAgent: userAgent);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Stripe webhook error: {ex.Message}");
            }
        }
    }
}
