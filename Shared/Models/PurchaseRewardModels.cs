namespace LittleHelperAI.Shared.Models
{
    /// <summary>
    /// Per-plan purchase reward settings configurable by admin
    /// </summary>
    public class PurchaseReferralSettings
    {
        public int Id { get; set; }
        public int PlanId { get; set; }
        public double ReferrerRewardCredits { get; set; } = 0;  // Credits for referrer when referee buys
        public double RefereeRewardCredits { get; set; } = 0;   // Credits for buyer (bonus)
        public double OwnerPurchaseRewardCredits { get; set; } = 0; // Credits for referrals when owner buys
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public StripePlan? Plan { get; set; }
    }

    /// <summary>
    /// Tracks each purchase-based reward transaction
    /// </summary>
    public class PurchaseReferralTransaction
    {
        public int Id { get; set; }
        public string AuditLogId { get; set; } = ""; // Links to CreditAuditLog
        public int PurchaserId { get; set; }         // User who made the purchase
        public int BeneficiaryId { get; set; }       // User who received the reward
        public int PlanId { get; set; }
        public string RewardType { get; set; } = ""; // REFERRER_REWARD, REFEREE_REWARD, OWNER_PURCHASE_REWARD
        public double CreditsAwarded { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// SECURITY: Audit log for ALL credit operations
    /// </summary>
    public class CreditAuditLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int UserId { get; set; }
        public string OperationType { get; set; } = ""; // PURCHASE, REFERRAL_SIGNUP, etc.
        public double CreditsAmount { get; set; }
        public double CreditsBefore { get; set; }
        public double CreditsAfter { get; set; }
        public string SourceType { get; set; } = "";
        public string? SourceReferenceId { get; set; }
        public int? RelatedUserId { get; set; }
        public string SecurityHash { get; set; } = "";
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public bool IsValidated { get; set; } = false;
        public DateTime? ValidatedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// SECURITY: Tokens to prevent replay attacks
    /// </summary>
    public class CreditSecurityToken
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int UserId { get; set; }
        public string TokenHash { get; set; } = "";
        public string OperationType { get; set; } = "";
        public bool IsUsed { get; set; } = false;
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ═══════════════════════════════════════════════════════════════
    // DTOs for Admin Panel
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// DTO for plan with its reward settings
    /// </summary>
    public class PlanRewardSettingsDto
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

    /// <summary>
    /// Request to update reward settings for a plan
    /// </summary>
    public class UpdatePlanRewardSettingsRequest
    {
        public int PlanId { get; set; }
        public double ReferrerRewardCredits { get; set; }
        public double RefereeRewardCredits { get; set; }
        public double OwnerPurchaseRewardCredits { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// DTO for purchase reward statistics
    /// </summary>
    public class PurchaseRewardStatsDto
    {
        public int TotalPurchaseRewards { get; set; }
        public double TotalCreditsAwarded { get; set; }
        public int UniqueReferrersBenefited { get; set; }
        public int UniqueRefereesBenefited { get; set; }
        public List<RecentPurchaseRewardDto> RecentRewards { get; set; } = new();
    }

    /// <summary>
    /// DTO for recent purchase reward
    /// </summary>
    public class RecentPurchaseRewardDto
    {
        public int Id { get; set; }
        public string PurchaserUsername { get; set; } = "";
        public string BeneficiaryUsername { get; set; } = "";
        public string PlanName { get; set; } = "";
        public string RewardType { get; set; } = "";
        public double CreditsAwarded { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// DTO for credit audit log entry (for admin viewing)
    /// </summary>
    public class CreditAuditLogDto
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
