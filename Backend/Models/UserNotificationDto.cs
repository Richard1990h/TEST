namespace LittleHelperAI.Backend.Models;

public sealed class UserNotificationDto
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ReadUtc { get; set; }
}
