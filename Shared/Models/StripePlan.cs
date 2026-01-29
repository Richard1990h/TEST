using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Shared.Models
{
    [Table("stripeplans")]
    public class StripePlan
    {
        [Key]
        public int Id { get; set; }

        // Wallet credits (one-time packs only)
        public int Credits { get; set; }

        // Stripe price id or "FREE"
        public string PriceId { get; set; } = "";

        // OneTime | Subscription | Free
        public string PlanType { get; set; } = "OneTime";

        // ✅ maps to plan_tier
        [Column("plan_tier")]
        public string PlanTier { get; set; } = "FREE";

        // ✅ maps to daily_credits
        [Column("daily_credits")]
        public int DailyCredits { get; set; } = 0;

        // ✅ maps to is_unlimited
        [Column("is_unlimited")]
        public bool IsUnlimited { get; set; } = false;

        // admin / UI
        public string? Description { get; set; }
    }
}
