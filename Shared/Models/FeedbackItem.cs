namespace LittleHelperAI.Shared.Models
{
    public class FeedbackItem
    {
        public int UserId { get; set; }             // ✅ Required for tracking who gave feedback
        public string Message { get; set; } = "";   // The original user message
        public string Response { get; set; } = "";  // The AI's reply
        public bool IsHelpful { get; set; }         // Thumbs up or down
    }
}
