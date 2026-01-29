using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Data;
using Stripe;
using Stripe.Checkout;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly StripeService _stripeService;
        private readonly IConfiguration _config;

        public PlansController(ApplicationDbContext db, StripeService stripeService, IConfiguration config)
        {
            _db = db;
            _stripeService = stripeService;
            _config = config;
        }

        // GET /api/plans
        [HttpGet]
        public async Task<ActionResult<List<StripePlan>>> GetPlans()
        {
            var plans = await _db.StripePlans.ToListAsync();
            return Ok(plans);
        }

        // GET /api/plans/userplans
        [HttpGet("userplans")]
        [Authorize]
        public async Task<ActionResult<List<UserPlan>>> GetUserPlans()
        {
            var userId = await GetCurrentUserIdAsync();
            var userPlans = await _db.UserPlans
                .Where(up => up.UserId == userId)
                .Include(up => up.Plan)
                .ToListAsync();

            return Ok(userPlans);
        }

        // POST /api/plans/create-checkout-session
        [HttpPost("create-checkout-session")]
        [Authorize]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
        {
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = request.PriceId,
                        Quantity = 1
                    }
                },
                Mode = request.Mode, // "payment" or "subscription"
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Ok(new StripeSessionDto
            {
                SessionId = session.Id
            });
        }

        // POST /api/plans/confirm-payment
        [HttpPost("confirm-payment")]
        [Authorize]
        public async Task<IActionResult> ConfirmPayment([FromQuery] string sessionId)
        {
            try
            {
                StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];

                var sessionService = new SessionService();
                var session = await sessionService.GetAsync(sessionId);

                if (session == null)
                    return BadRequest("Invalid Stripe session.");

                var lineItemService = new SessionLineItemService();
                var lineItems = await lineItemService.ListAsync(session.Id);

                if (lineItems.Data == null || !lineItems.Data.Any())
                    return BadRequest("No items found in session.");

                var priceId = lineItems.Data[0].Price.Id;

                var plan = await _db.StripePlans.FirstOrDefaultAsync(p => p.PriceId == priceId);
                if (plan == null)
                    return BadRequest("Plan not found for PriceId.");

                var userId = await GetCurrentUserIdAsync();
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return Unauthorized("User not found.");

                // ✅ Add credits from plan
                user.Credits += plan.Credits;

                // ✅ Save user's plan history
                var userPlan = new UserPlan
                {
                    UserId = user.Id,
                    PlanId = plan.Id,
                    PurchasedAt = DateTime.UtcNow,
                    CreditsAdded = plan.Credits
                };

                _db.UserPlans.Add(userPlan);
                await _db.SaveChangesAsync();

                return Ok(new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Credits = user.Credits,
                    Role = user.Role
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ConfirmPayment error: {ex.Message}");
                return StatusCode(500, "Server error during confirming payment.");
            }
        }

        // Utility to get the logged-in user's ID
        private async Task<int> GetCurrentUserIdAsync()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                throw new UnauthorizedAccessException("Username not found.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                throw new UnauthorizedAccessException("User not found.");

            return user.Id;
        }

        // DTOs
        public class CreateCheckoutSessionRequest
        {
            public string PriceId { get; set; } = "";
            public string Mode { get; set; } = "payment"; // or "subscription"
            public string SuccessUrl { get; set; } = "";
            public string CancelUrl { get; set; } = "";
        }

        public class StripeSessionDto
        {
            public string SessionId { get; set; } = "";
        }
    }
}
