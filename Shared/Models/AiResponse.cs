namespace LittleHelperAI.Shared.Models
{
    /// <summary>
    /// Unified AI response DTO for all AI operations
    /// </summary>
    public class AiResponse
    {
        /// <summary>
        /// Whether the request was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The main content/response from the AI
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Raw AI output (before parsing)
        /// </summary>
        public string? RawContent { get; set; }

        /// <summary>
        /// Error message if the request failed
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Type of request that was processed
        /// </summary>
        public string RequestType { get; set; } = "";

        /// <summary>
        /// Timestamp of the response
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Additional metadata in the response
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
