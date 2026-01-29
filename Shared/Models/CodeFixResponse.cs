namespace LittleHelperAI.Shared.Models
{
    /// <summary>
    /// Structured response for code fixing operations
    /// </summary>
    public class CodeFixResponse
    {
        /// <summary>
        /// The full corrected code
        /// </summary>
        public string FixedCode { get; set; } = "";

        /// <summary>
        /// List of issues found in the original code
        /// </summary>
        public List<string> IssuesFound { get; set; } = new();

        /// <summary>
        /// List of changes made to fix the code
        /// </summary>
        public List<string> ChangesMade { get; set; } = new();

        /// <summary>
        /// High-level explanation of what was wrong and why
        /// </summary>
        public string Explanation { get; set; } = "";

        /// <summary>
        /// Whether the code was actually modified
        /// </summary>
        public bool WasModified { get; set; }

        /// <summary>
        /// The original code (for comparison)
        /// </summary>
        public string? OriginalCode { get; set; }

        /// <summary>
        /// Detected programming language
        /// </summary>
        public string? Language { get; set; }
    }
}
