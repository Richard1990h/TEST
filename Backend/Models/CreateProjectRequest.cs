namespace LittleHelperAI.Backend.Models;

public sealed class CreateProjectRequest
{
    public required string Prompt { get; init; }
    
    /// <summary>
    /// Session ID for conversation continuity.
    /// If not provided, a new session will be created.
    /// </summary>
    public string? SessionId { get; init; }
    
    /// <summary>
    /// User ID for credit deduction.
    /// </summary>
    public int UserId { get; init; }
}
