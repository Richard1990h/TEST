using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Shared.Models;

[Table("stripeplan_policies")]
public sealed class StripePlanPolicy
{
    [Key]
    [Column("plan_id")]
    public int PlanId { get; set; }

    // Human-readable name (Admin/UI)
    [Column("plan_name")]
    public string PlanName { get; set; } = "";

    // 🔽 NEW — authoritative tier
    [Column("plan_tier")]
    public string PlanTier { get; set; } = "FREE";

    // Unlimited behaviour override
    [Column("is_unlimited")]
    public bool IsUnlimited { get; set; }

    // Daily auto-granted credits (FREE / BASIC / PRO)
    [Column("daily_credits")]
    public double? DailyCredits { get; set; }
}
