namespace LittleHelperAI.KingFactory.Models;

/// <summary>
/// Represents a single message in the conversation.
/// </summary>
public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "user"; // user, assistant, system, tool
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ToolCallId { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Represents a tool invocation request from the LLM.
/// </summary>
public class ToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
}

/// <summary>
/// Represents the result of a tool execution.
/// </summary>
public class ToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
    public TimeSpan ExecutionTime { get; set; }
}
