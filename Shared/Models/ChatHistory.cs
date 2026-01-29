using System;

namespace LittleHelperAI.Shared.Models
{
    public class ChatHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public string Message { get; set; } = string.Empty;
        public string? Reply { get; set; }

        public DateTime Timestamp { get; set; }
        public string Title { get; set; } = "";

        public Guid ConversationId { get; set; }

        // 🔢 LLM USAGE (NEW — SAFE DEFAULTS)
        public int PromptTokens { get; set; } = 0;
        public int CompletionTokens { get; set; } = 0;

        // 💰 COST TRACKING
        public double Cost { get; set; } = 0;

        // 📦 RICH MESSAGE DATA (JSON serialized)
        // Stores all message metadata: Kind, FileName, OriginalCode, FixedCode, 
        // ProjectSessionId, ProjectName, FileCount, AnalysisResults, etc.
        public string? Metadata { get; set; }
    }
}
