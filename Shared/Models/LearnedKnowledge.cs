using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Shared.Models
{
    // Auto-learned Q/A pairs written back after successful answers
    [Table("learned_knowledge")]
    public class LearnedKnowledge
    {
        public int Id { get; set; }

        // maps to user_id
        [Column("user_id")]
        public int UserId { get; set; }

        // maps to topic
        [Column("topic")]
        public string Topic { get; set; } = "general";

        // maps to normalized_key
        [Column("normalized_key")]
        public string NormalizedKey { get; set; } = "";

        // ❌ column does NOT exist in DB
        [NotMapped]
        public string Question { get; set; } = "";

        // maps to answer
        [Column("answer")]
        public string Answer { get; set; } = "";

        // alias only
        [NotMapped]
        public string Information
        {
            get => Answer;
            set => Answer = value;
        }

        // maps to source
        [Column("source")]
        public string Source { get; set; } = "llm";

        // maps to confidence
        [Column("confidence")]
        public double Confidence { get; set; } = 0.55;

        // maps to created_at
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // maps to last_used_at
        [Column("last_used_at")]
        public DateTime? LastUsedAt { get; set; }

        // maps to times_used
        [Column("times_used")]
        public int TimesUsed { get; set; } = 0;

        // maps to last_verified_at
        [Column("last_verified_at")]
        public DateTime? LastVerifiedAt { get; set; }
    }
}
