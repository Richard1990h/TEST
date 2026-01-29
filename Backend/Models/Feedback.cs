namespace LittleHelperAI.Models;

public class Feedback
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public bool IsHelpful { get; set; }
    public DateTime CreatedAt { get; set; }
}
