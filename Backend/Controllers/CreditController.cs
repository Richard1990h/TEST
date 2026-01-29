using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using Stripe;
using Stripe.Checkout;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class CreditController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        public CreditController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // ➕ Add credits manually (internal)
        [HttpPost("add-credits")]
        public async Task<IActionResult> AddCredits(int userId, int amount)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found" });

            user.Credits += amount;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Credits
            });
        }

        // ➖ Remove credits manually (admin only)
        [HttpPost("remove-credits")]
        public async Task<IActionResult> RemoveCredits(int userId, double amount)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found" });

            user.Credits = Math.Max(0, user.Credits - amount);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Credits
            });
        }

        // 🔍 Get all users
        [HttpGet("all")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.Users
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.Credits,
                    u.Role
                })
                .ToListAsync();

            return Ok(users);
        }

        // 📦 Get all Stripe plans
        [HttpGet("plans")]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _context.StripePlans
                .OrderBy(p => p.Credits)
                .ToListAsync();

            return Ok(plans);
        }

        // 💳 Create Stripe Checkout Session
        [HttpPost("create-checkout-session")]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] PaymentRequest request)
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
                        Quantity = 1,
                    },
                },
                Mode = request.Mode,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Ok(new { sessionId = session.Id });
        }

        // ✅ Confirm payment after Stripe Checkout success
        [HttpPost("confirm-payment")]
        public async Task<IActionResult> ConfirmPayment(string sessionId, int userId)
        {
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];

            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(sessionId, new SessionGetOptions
            {
                Expand = new List<string> { "line_items.data.price" }
            });

            var priceId = session.LineItems.Data.FirstOrDefault()?.Price?.Id;

            if (string.IsNullOrEmpty(priceId))
                return BadRequest(new { message = "PriceId not found in session." });

            var plan = await _context.StripePlans.FirstOrDefaultAsync(p => p.PriceId == priceId);
            if (plan == null)
                return BadRequest(new { message = "Plan not found for the paid PriceId." });

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            user.Credits += plan.Credits;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Credits,
                AddedCredits = plan.Credits
            });
        }

        // 📦 PaymentRequest model (inline for clarity)
        public class PaymentRequest
        {
            public string PriceId { get; set; } = "";
            public string Mode { get; set; } = "payment"; // or "subscription"
            public string SuccessUrl { get; set; } = "";
            public string CancelUrl { get; set; } = "";
        }
    }
}
