using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Shared.Models;

[Table("user_stripe_subscriptions")]
public sealed class UserStripeSubscription
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("plan_id")]
    public int PlanId { get; set; }

    [Column("price_id")]
    public string PriceId { get; set; } = "";

    [Column("subscription_id")]
    public string SubscriptionId { get; set; } = "";

    [Column("status")]
    public string Status { get; set; } = "active"; // active, trialing, past_due, canceled, unpaid

    [Column("current_period_end_utc")]
    public DateTime CurrentPeriodEndUtc { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
