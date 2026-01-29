using System;

namespace LittleHelperAI.Shared.Models
{
    // Dictionary-like knowledge items (fast lookup before LLM)
    public class KnowledgeEntry
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";          // normalized key (e.g. "polymorphism")
        public string Category { get; set; } = "general";
        public string Answer { get; set; } = "";
        public string Aliases { get; set; } = "";      // comma-separated aliases
        public double Confidence { get; set; } = 0.6;  // 0..1
        public string Source { get; set; } = "manual"; // manual|web|llm
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; }
        public int TimesUsed { get; set; } = 0;
    }
}
