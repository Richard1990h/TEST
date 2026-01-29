using System;

namespace LittleHelperAI.Shared.Models
{
    // Structured facts (versions, dates, specs). Great for "latest", "version", "today" questions.
    public class FactEntry
    {
        public int Id { get; set; }
        public string Subject { get; set; } = "";     // e.g. "Unreal Engine"
        public string Property { get; set; } = "";    // e.g. "latest_version"
        public string Value { get; set; } = "";       // e.g. "5.4"
        public string Source { get; set; } = "manual";// manual|web|llm
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ValidUntil { get; set; }      // if stale, trigger web refresh
        public DateTime? LastUsedAt { get; set; }
        public int TimesUsed { get; set; } = 0;
    }
}
