using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Services
{
    /// <summary>
    /// Service to handle purchase-based referral rewards
    /// Called when a user completes a purchase
    /// </summary>
    public interface IPurchaseRewardService
    {
        Task ProcessPurchaseRewards(int purchaserId, int planId, string? ipAddress = null, string? userAgent = null);
    }

    public class PurchaseRewardService : IPurchaseRewardService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICreditSecurityService _securityService;
        private readonly ILogger<PurchaseRewardService> _logger;

        public PurchaseRewardService(
            ApplicationDbContext context,
            ICreditSecurityService securityService,
            ILogger<PurchaseRewardService> logger)
        {
            _context = context;
            _securityService = securityService;
            _logger = logger;
        }

        /// <summary>
        /// Process all purchase-based rewards when a user buys a plan
        /// </summary>
        public async Task ProcessPurchaseRewards(int purchaserId, int planId, string? ipAddress = null, string? userAgent = null)
        {
            _logger.LogInformation("Processing purchase rewards for user {UserId}, plan {PlanId}", purchaserId, planId);

            // Get reward settings for this plan
            var settings = await _context.PurchaseReferralSettings
                .FirstOrDefaultAsync(s => s.PlanId == planId && s.IsEnabled);

            if (settings == null)
            {
                _logger.LogInformation("No purchase reward settings found for plan {PlanId}", planId);
                return;
            }

            var purchaser = await _context.Users.FindAsync(purchaserId);
            if (purchaser == null)
            {
                _logger.LogWarning("Purchaser {UserId} not found", purchaserId);
                return;
            }

            // SCENARIO 1: Purchaser was referred by someone - reward both referrer and purchaser
            if (purchaser.ReferredByUserId.HasValue && purchaser.ReferredByUserId.Value > 0)
            {
                var referrer = await _context.Users.FindAsync(purchaser.ReferredByUserId.Value);
                if (referrer != null)
                {
                    // Reward the referrer (person who shared the code)
                    if (settings.ReferrerRewardCredits > 0)
                    {
                        await AwardPurchaseReward(
                            purchaserId,
                            referrer.Id,
                            planId,
                            "REFERRER_REWARD",
                            settings.ReferrerRewardCredits,
                            "PURCHASE_REFERRER_REWARD",
                            ipAddress,
                            userAgent);

                        _logger.LogInformation(
                            "Awarded {Credits} credits to referrer {ReferrerId} for purchase by {PurchaserId}",
                            settings.ReferrerRewardCredits, referrer.Id, purchaserId);
                    }

                    // Reward the purchaser (buyer bonus)
                    if (settings.RefereeRewardCredits > 0)
                    {
                        await AwardPurchaseReward(
                            purchaserId,
                            purchaserId,
                            planId,
                            "REFEREE_REWARD",
                            settings.RefereeRewardCredits,
                            "PURCHASE_REFEREE_REWARD",
                            ipAddress,
                            userAgent);

                        _logger.LogInformation(
                            "Awarded {Credits} bonus credits to purchaser {PurchaserId} (referred user bonus)",
                            settings.RefereeRewardCredits, purchaserId);
                    }
                }
            }

            // SCENARIO 2: Purchaser is a referral code owner - reward all their referrals
            if (settings.OwnerPurchaseRewardCredits > 0)
            {
                var referrals = await _context.Users
                    .Where(u => u.ReferredByUserId == purchaserId)
                    .ToListAsync();

                if (referrals.Any())
                {
                    _logger.LogInformation(
                        "Awarding {Credits} credits to {Count} referrals of purchaser {PurchaserId}",
                        settings.OwnerPurchaseRewardCredits, referrals.Count, purchaserId);

                    foreach (var referral in referrals)
                    {
                        await AwardPurchaseReward(
                            purchaserId,
                            referral.Id,
                            planId,
                            "OWNER_PURCHASE_REWARD",
                            settings.OwnerPurchaseRewardCredits,
                            "PURCHASE_OWNER_REWARD",
                            ipAddress,
                            userAgent);

                        _logger.LogInformation(
                            "Awarded {Credits} credits to referral {ReferralId} because owner {OwnerId} made a purchase",
                            settings.OwnerPurchaseRewardCredits, referral.Id, purchaserId);
                    }
                }
            }
        }

        /// <summary>
        /// Award credits with full security audit
        /// </summary>
        private async Task AwardPurchaseReward(
            int purchaserId,
            int beneficiaryId,
            int planId,
            string rewardType,
            double credits,
            string operationType,
            string? ipAddress,
            string? userAgent)
        {
            // Create audited credit operation (handles security hash, validation, etc.)
            var auditLog = await _securityService.CreateAuditedCreditOperation(
                userId: beneficiaryId,
                operationType: operationType,
                creditsAmount: credits,
                sourceType: "PURCHASE_REFERRAL_REWARD",
                sourceReferenceId: $"plan:{planId}:purchaser:{purchaserId}",
                relatedUserId: purchaserId,
                ipAddress: ipAddress,
                userAgent: userAgent);

            // Record the transaction
            var transaction = new PurchaseReferralTransaction
            {
                AuditLogId = auditLog.Id,
                PurchaserId = purchaserId,
                BeneficiaryId = beneficiaryId,
                PlanId = planId,
                RewardType = rewardType,
                CreditsAwarded = credits,
                CreatedAt = DateTime.UtcNow
            };

            _context.PurchaseReferralTransactions.Add(transaction);
            await _context.SaveChangesAsync();
        }
    }
}
