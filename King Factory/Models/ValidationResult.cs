namespace LittleHelperAI.KingFactory.Models;

/// <summary>
/// Result of validation pass.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationIssue> Issues { get; set; } = new();
    public string? Summary { get; set; }
    public bool RequiresRetry { get; set; }
    public string? SuggestedFix { get; set; }

    /// <summary>
    /// Create a valid result.
    /// </summary>
    public static ValidationResult Valid() => new() { IsValid = true };

    /// <summary>
    /// Create an invalid result with a message.
    /// </summary>
    public static ValidationResult Invalid(string message) => new()
    {
        IsValid = false,
        Issues = new List<ValidationIssue>
        {
            new() { Severity = ValidationSeverity.Error, Message = message }
        },
        Summary = message
    };
}

public class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Code { get; set; }
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Result of reflection analysis.
/// </summary>
public class ReflectionResult
{
    public bool GoalAchieved { get; set; }
    public double ConfidenceScore { get; set; }
    public List<string> Observations { get; set; } = new();
    public List<string> Improvements { get; set; } = new();
    public bool ShouldRetry { get; set; }
    public string? RetryStrategy { get; set; }
}
