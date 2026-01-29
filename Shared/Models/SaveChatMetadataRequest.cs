using System.Collections.Generic;

namespace LittleHelperAI.Shared.Models
{
    public class SaveChatMetadataRequest
    {
        public int UserId { get; set; }
        public int? ChatId { get; set; }
        public string? UserMessage { get; set; }
        public string? AiResponse { get; set; }
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
