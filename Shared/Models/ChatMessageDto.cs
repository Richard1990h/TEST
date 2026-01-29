using System;
using System.Collections.Generic;

namespace LittleHelperAI.Shared.Models
{
    public class ChatMessageDto
    {
        public string Text { get; set; } = string.Empty;
        public bool IsUser { get; set; }
        public DateTime Timestamp { get; set; }
        
        // Metadata fields
        public string? Kind { get; set; }
        public string? FileName { get; set; }
        public string? OriginalCode { get; set; }
        public string? FixedCode { get; set; }
        public string? Language { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int IssueCount { get; set; }
        public List<string>? CompilerErrors { get; set; }
        public List<string>? CompilerWarnings { get; set; }
        public List<string>? StaticIssues { get; set; }
        public List<string>? Suggestions { get; set; }
        public bool FixAttempted { get; set; }
        public bool FixApplied { get; set; }
        public string? ProjectSessionId { get; set; }
        public string? ProjectName { get; set; }
        public int FileCount { get; set; }
        public bool IsProjectCreation { get; set; }
    }
}
