using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/referral")]
    public class ReferralController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReferralController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─────────────────────────────────────────────────────────────
        // GET USER'S REFERRAL INFO
        // ─────────────────────────────────────────────────────────────
        [HttpGet("info/{userId}")]
        public async Task<IActionResult> GetUserReferralInfo(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found");

            // Ensure user has a referral code
            if (string.IsNullOrEmpty(user.ReferralCode))
            {
                user.ReferralCode = GenerateReferralCode(user.Id, user.Username);
                await _context.SaveChangesAsync();
            }

            // Get settings
            var settings = await _context.ReferralSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new ReferralSettings
                {
                    ReferrerCredits = 50.0,
                    RefereeCredits = 25.0,
                    IsEnabled = true
                };
            }

            // Get user's referral stats
            var transactions = await _context.ReferralTransactions
                .Where(t => t.ReferrerId == userId)
                .ToListAsync();

            return Ok(new UserReferralInfoDto
            {
                ReferralCode = user.ReferralCode,
                TotalReferrals = transactions.Count,
                TotalCreditsEarned = transactions.Sum(t => t.ReferrerCreditsAwarded),
                CurrentReferrerReward = settings.ReferrerCredits,
                CurrentRefereeReward = settings.RefereeCredits,
                IsEnabled = settings.IsEnabled
            });
        }

        // ─────────────────────────────────────────────────────────────
        // VALIDATE REFERRAL CODE (for registration form)
        // ─────────────────────────────────────────────────────────────
        [HttpGet("validate/{code}")]
        public async Task<IActionResult> ValidateReferralCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest("Referral code is required");

            var referrer = await _context.Users
                .FirstOrDefaultAsync(u => u.ReferralCode.ToLower() == code.ToLower());

            if (referrer == null)
                return NotFound(new { valid = false, message = "Invalid referral code" });

            var settings = await _context.ReferralSettings.FirstOrDefaultAsync();
            var bonusCredits = settings?.RefereeCredits ?? 25.0;

            return Ok(new 
            { 
                valid = true, 
                referrerUsername = referrer.Username,
                bonusCredits = bonusCredits,
                message = $"You'll receive {bonusCredits} bonus credits when you sign up!"
            });
        }

        // ─────────────────────────────────────────────────────────────
        // REGENERATE REFERRAL CODE
        // ─────────────────────────────────────────────────────────────
        [HttpPost("regenerate/{userId}")]
        public async Task<IActionResult> RegenerateCode(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found");

            user.ReferralCode = GenerateReferralCode(user.Id, user.Username);
            await _context.SaveChangesAsync();

            return Ok(new { referralCode = user.ReferralCode });
        }

        // ─────────────────────────────────────────────────────────────
        // HELPER: Generate unique referral code
        // ─────────────────────────────────────────────────────────────
        private string GenerateReferralCode(int userId, string username)
        {
            // Create a code like: JOHN-A3X9
            var prefix = username.Length >= 4 
                ? username.Substring(0, 4).ToUpper() 
                : username.ToUpper().PadRight(4, 'X');
            
            var suffix = Convert.ToBase64String(BitConverter.GetBytes(userId + DateTime.UtcNow.Ticks))
                .Replace("/", "")
                .Replace("+", "")
                .Replace("=", "")
                .Substring(0, 4)
                .ToUpper();

            return $"{prefix}-{suffix}";
        }
    }
}
