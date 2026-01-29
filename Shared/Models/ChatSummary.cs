namespace LittleHelperAI.Shared.Models
{
    public class ChatSummary
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        public string Message { get; set; } = string.Empty;
        public string Reply { get; set; } = string.Empty;
    }
}
