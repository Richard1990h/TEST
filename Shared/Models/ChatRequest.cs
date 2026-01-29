namespace LittleHelperAI.Shared.Models
{
    public class ChatRequest
    {
        public int UserId { get; set; }
        public string Message { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // =====================================================
        // 🔑 CHAT IDENTITY (SOURCE OF TRUTH)
        // =====================================================

        /// <summary>
        /// Existing chat to continue. REQUIRED when continuing.
        /// </summary>
        public int? ChatId { get; set; }

        /// <summary>
        /// Explicitly start a brand new chat.
        /// Must be true to create a new ChatHistory row.
        /// </summary>
        public bool CreateNewChat { get; set; } = false;

        // =====================================================
        // OPTIONAL (future-safe, not used yet)
        // =====================================================
        public Guid? ConversationId { get; set; }

        // =====================================================
        // FILES
        // =====================================================
        public List<FileAttachment>? Files { get; set; }
    }

    public class FileAttachment
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
