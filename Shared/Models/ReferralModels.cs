namespace LittleHelperAI.Shared.Models
{
    /// <summary>
    /// Stores the referral/reward settings configurable by admin
    /// </summary>
    public class ReferralSettings
    {
        public int Id { get; set; } = 1; // Singleton row
        public double ReferrerCredits { get; set; } = 50.0; // Credits for the person who referred
        public double RefereeCredits { get; set; } = 25.0;  // Credits for the new user who used a code
        public bool IsEnabled { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Tracks each referral transaction
    /// </summary>
    public class ReferralTransaction
    {
        public int Id { get; set; }
        public int ReferrerId { get; set; }        // User who referred
        public int RefereeId { get; set; }         // New user who signed up
        public string ReferralCode { get; set; } = "";
        public double ReferrerCreditsAwarded { get; set; }
        public double RefereeCreditsAwarded { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// DTO for admin to view referral stats
    /// </summary>
    public class ReferralStatsDto
    {
        public int TotalReferrals { get; set; }
        public double TotalCreditsAwarded { get; set; }
        public int ActiveReferrers { get; set; }
        public List<TopReferrerDto> TopReferrers { get; set; } = new();
        public List<RecentReferralDto> RecentReferrals { get; set; } = new();
    }

    public class TopReferrerDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public int ReferralCount { get; set; }
        public double TotalCreditsEarned { get; set; }
    }

    public class RecentReferralDto
    {
        public int Id { get; set; }
        public string ReferrerUsername { get; set; } = "";
        public string RefereeUsername { get; set; } = "";
        public string ReferralCode { get; set; } = "";
        public double ReferrerCredits { get; set; }
        public double RefereeCredits { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// DTO for updating referral settings
    /// </summary>
    public class UpdateReferralSettingsRequest
    {
        public double ReferrerCredits { get; set; }
        public double RefereeCredits { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// DTO for user's referral info
    /// </summary>
    public class UserReferralInfoDto
    {
        public string ReferralCode { get; set; } = "";
        public int TotalReferrals { get; set; }
        public double TotalCreditsEarned { get; set; }
        public double CurrentReferrerReward { get; set; }
        public double CurrentRefereeReward { get; set; }
        public bool IsEnabled { get; set; }
    }
}
