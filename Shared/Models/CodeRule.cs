using System;

namespace LittleHelperAI.Shared.Models
{
    // Deterministic fixes keyed by compiler error codes (fast code-fixing without LLM)
    public class CodeRule
    {
        public int Id { get; set; }
        public string Language { get; set; } = "csharp";
        public string ErrorCode { get; set; } = ""; // e.g. CS1002
        public string Pattern { get; set; } = "";   // optional
        public string FixExplanation { get; set; } = "";
        public string FixTemplate { get; set; } = ""; // optional template or guidance
        public string Source { get; set; } = "manual";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
