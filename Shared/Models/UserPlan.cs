using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Shared.Models
{
    public class UserPlan
    {
        public int Id { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("plan_id")]
        public int PlanId { get; set; }

        public StripePlan Plan { get; set; } = new();

        [Column("purchased_at")]
        public DateTime PurchasedAt { get; set; }

        [Column("credits_added")]
        public int CreditsAdded { get; set; } = 0;
    }
}
