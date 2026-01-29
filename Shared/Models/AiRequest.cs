namespace LittleHelperAI.Shared.Models
{
    /// <summary>
    /// Unified AI request DTO for all AI operations
    /// </summary>
    public class AiRequest
    {
        /// <summary>
        /// Type of AI request: chat, code_analysis, code_fix, project_scan, project_generate
        /// </summary>
        public string RequestType { get; set; } = "chat";

        /// <summary>
        /// The prompt or input for the AI
        /// </summary>
        public string Prompt { get; set; } = "";

        /// <summary>
        /// Maximum number of retries (default: 2)
        /// </summary>
        public int? MaxRetries { get; set; }

        /// <summary>
        /// Additional metadata for the request
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Timeout in seconds (default: 30)
        /// </summary>
        public int? TimeoutSeconds { get; set; }
    }
}
