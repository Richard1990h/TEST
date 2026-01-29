using LittleHelperAI.KingFactory.Models;

namespace LittleHelperAI.KingFactory.Tools;

/// <summary>
/// Interface for all tools that can be invoked by the AI.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Unique name of the tool (used in tool calls).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description of what the tool does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON schema for the tool's parameters.
    /// </summary>
    ToolSchema Schema { get; }

    /// <summary>
    /// Whether this tool requires user confirmation before execution.
    /// </summary>
    bool RequiresConfirmation { get; }

    /// <summary>
    /// Execute the tool with the given arguments.
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        Dictionary<string, object> arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate arguments before execution.
    /// </summary>
    ValidationResult ValidateArguments(Dictionary<string, object> arguments);
}

/// <summary>
/// Schema definition for a tool's parameters.
/// </summary>
public class ToolSchema
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ToolParameter> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

/// <summary>
/// Definition of a single tool parameter.
/// </summary>
public class ToolParameter
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = string.Empty;
    public object? Default { get; set; }
    public List<string>? Enum { get; set; }
}

/// <summary>
/// Simple validation result for tool arguments.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();

    public static ValidationResult Valid() => new() { IsValid = true };
    public static ValidationResult Invalid(string error) => new() { IsValid = false, Errors = { error } };
    public static ValidationResult Invalid(IEnumerable<string> errors) => new() { IsValid = false, Errors = errors.ToList() };
}
