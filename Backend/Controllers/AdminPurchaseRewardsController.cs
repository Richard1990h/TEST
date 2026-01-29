using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Backend.Services;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/admin/purchase-rewards")]
    public class AdminPurchaseRewardsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AdminPurchaseRewardsController> _logger;

        public AdminPurchaseRewardsController(
            ApplicationDbContext context,
            ILogger<AdminPurchaseRewardsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        // GET ALL PLANS WITH THEIR REWARD SETTINGS
        // ─────────────────────────────────────────────────────────────
        [HttpGet("plans")]
        public async Task<IActionResult> GetPlansWithRewardSettings()
        {
            try
            {
                var plans = await _context.StripePlans.AsNoTracking().ToListAsync();
                var settings = await _context.PurchaseReferralSettings.AsNoTracking().ToListAsync();

                var result = plans
                    .Select(plan =>
                    {
                        var setting = settings.FirstOrDefault(s => s.PlanId == plan.Id);

                        return new PlanRewardSettingsDto
                        {
                            PlanId = plan.Id,
                            PlanName = $"{plan.Credits} Credits ({plan.PlanType})",
                            Credits = plan.Credits,
                            PlanType = plan.PlanType,
                            ReferrerRewardCredits = setting != null ? setting.ReferrerRewardCredits : 0,
                            RefereeRewardCredits = setting != null ? setting.RefereeRewardCredits : 0,
                            OwnerPurchaseRewardCredits = setting != null ? setting.OwnerPurchaseRewardCredits : 0,
                            IsEnabled = setting != null ? setting.IsEnabled : true
                        };
                    })
                    .OrderBy(p => p.PlanType)
                    .ThenBy(p => p.Credits)
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching plans with reward settings");
                return StatusCode(500, "Failed to load plans");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // UPDATE REWARD SETTINGS FOR A PLAN
        // ─────────────────────────────────────────────────────────────
        [HttpPost("plans/update")]
        public async Task<IActionResult> UpdatePlanRewardSettings(
            [FromBody] UpdatePlanRewardSettingsRequest request)
        {
            try
            {
                var plan = await _context.StripePlans.FindAsync(request.PlanId);
                if (plan == null)
                    return NotFound($"Plan {request.PlanId} not found");

                var setting = await _context.PurchaseReferralSettings
                    .FirstOrDefaultAsync(s => s.PlanId == request.PlanId);

                if (setting == null)
                {
                    setting = new PurchaseReferralSettings
                    {
                        PlanId = request.PlanId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.PurchaseReferralSettings.Add(setting);
                }

                setting.ReferrerRewardCredits = request.ReferrerRewardCredits;
                setting.RefereeRewardCredits = request.RefereeRewardCredits;
                setting.OwnerPurchaseRewardCredits = request.OwnerPurchaseRewardCredits;
                setting.IsEnabled = request.IsEnabled;
                setting.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(new { message = "Settings updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating plan reward settings");
                return StatusCode(500, "Failed to update settings");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET PURCHASE REWARD STATISTICS
        // ─────────────────────────────────────────────────────────────
        [HttpGet("stats")]
        public async Task<IActionResult> GetPurchaseRewardStats()
        {
            try
            {
                var transactions = await _context.PurchaseReferralTransactions.AsNoTracking().ToListAsync();
                var users = await _context.Users.AsNoTracking().ToListAsync();
                var plans = await _context.StripePlans.AsNoTracking().ToListAsync();

                var stats = new PurchaseRewardStatsDto
                {
                    TotalPurchaseRewards = transactions.Count,
                    TotalCreditsAwarded = transactions.Sum(t => t.CreditsAwarded),
                    UniqueReferrersBenefited = transactions
                        .Where(t => t.RewardType == "REFERRER_REWARD")
                        .Select(t => t.BeneficiaryId)
                        .Distinct()
                        .Count(),
                    UniqueRefereesBenefited = transactions
                        .Where(t =>
                            t.RewardType == "REFEREE_REWARD" ||
                            t.RewardType == "OWNER_PURCHASE_REWARD")
                        .Select(t => t.BeneficiaryId)
                        .Distinct()
                        .Count(),
                    RecentRewards = transactions
                        .OrderByDescending(t => t.CreatedAt)
                        .Take(20)
                        .Select(t =>
                        {
                            var purchaser = users.FirstOrDefault(u => u.Id == t.PurchaserId);
                            var beneficiary = users.FirstOrDefault(u => u.Id == t.BeneficiaryId);
                            var plan = plans.FirstOrDefault(p => p.Id == t.PlanId);

                            return new RecentPurchaseRewardDto
                            {
                                Id = t.Id,
                                PurchaserUsername = purchaser != null ? purchaser.Username : "Unknown",
                                BeneficiaryUsername = beneficiary != null ? beneficiary.Username : "Unknown",
                                PlanName = plan != null ? $"{plan.Credits} Credits" : "Unknown",
                                RewardType = t.RewardType,
                                CreditsAwarded = t.CreditsAwarded,
                                CreatedAt = t.CreatedAt
                            };
                        })
                        .ToList()
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching purchase reward stats");
                return StatusCode(500, "Failed to load stats");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET CREDIT AUDIT LOG
        // ─────────────────────────────────────────────────────────────
        [HttpGet("audit-log")]
        public async Task<IActionResult> GetCreditAuditLog(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var users = await _context.Users.AsNoTracking().ToListAsync();

                var logs = await _context.CreditAuditLogs
                    .AsNoTracking()
                    .OrderByDescending(l => l.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var result = logs.Select(l =>
                {
                    var user = users.FirstOrDefault(u => u.Id == l.UserId);

                    return new CreditAuditLogDto
                    {
                        Id = l.Id,
                        Username = user != null ? user.Username : "Unknown",
                        OperationType = l.OperationType,
                        CreditsAmount = l.CreditsAmount,
                        CreditsBefore = l.CreditsBefore,
                        CreditsAfter = l.CreditsAfter,
                        SourceType = l.SourceType,
                        IsValidated = l.IsValidated,
                        CreatedAt = l.CreatedAt
                    };
                }).ToList();

                var totalCount = await _context.CreditAuditLogs.CountAsync();

                return Ok(new { data = result, total = totalCount, page, pageSize });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching credit audit log");
                return StatusCode(500, "Failed to load audit log");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // VALIDATE CREDIT AUDIT ENTRY
        // ─────────────────────────────────────────────────────────────
        [HttpPost("audit-log/validate/{auditId}")]
        public async Task<IActionResult> ValidateAuditEntry(
            string auditId,
            [FromServices] ICreditSecurityService securityService)
        {
            try
            {
                var isValid = await securityService.ValidateAuditEntry(auditId);
                return Ok(new { valid = isValid });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating audit entry {AuditId}", auditId);
                return StatusCode(500, "Validation failed");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // DTOs & REQUEST MODELS (ADMIN ONLY)
    // ─────────────────────────────────────────────────────────────

    public sealed class PlanRewardSettingsDto
    {
        public int PlanId { get; set; }
        public string PlanName { get; set; } = "";
        public int Credits { get; set; }
        public string PlanType { get; set; } = "";
        public double ReferrerRewardCredits { get; set; }
        public double RefereeRewardCredits { get; set; }
        public double OwnerPurchaseRewardCredits { get; set; }
        public bool IsEnabled { get; set; }
    }

    public sealed class UpdatePlanRewardSettingsRequest
    {
        public int PlanId { get; set; }
        public double ReferrerRewardCredits { get; set; }
        public double RefereeRewardCredits { get; set; }
        public double OwnerPurchaseRewardCredits { get; set; }
        public bool IsEnabled { get; set; }
    }

    public sealed class PurchaseRewardStatsDto
    {
        public int TotalPurchaseRewards { get; set; }
        public double TotalCreditsAwarded { get; set; }
        public int UniqueReferrersBenefited { get; set; }
        public int UniqueRefereesBenefited { get; set; }
        public List<RecentPurchaseRewardDto> RecentRewards { get; set; } = new();
    }

    public sealed class RecentPurchaseRewardDto
    {
        public int Id { get; set; }
        public string PurchaserUsername { get; set; } = "";
        public string BeneficiaryUsername { get; set; } = "";
        public string PlanName { get; set; } = "";
        public string RewardType { get; set; } = "";
        public double CreditsAwarded { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class CreditAuditLogDto
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string OperationType { get; set; } = "";
        public double CreditsAmount { get; set; }
        public double CreditsBefore { get; set; }
        public double CreditsAfter { get; set; }
        public string SourceType { get; set; } = "";
        public bool IsValidated { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
