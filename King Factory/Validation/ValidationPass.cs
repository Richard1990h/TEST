using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace LittleHelperAI.KingFactory.Validation;

/// <summary>
/// Validates AI outputs before returning to user.
/// </summary>
public interface IValidationPass
{
    /// <summary>
    /// Validate an output.
    /// </summary>
    ValidationPassResult Validate(string output, ValidationContext context);

    /// <summary>
    /// Validate tool arguments before execution.
    /// </summary>
    ValidationResult ValidateToolCall(string toolName, Dictionary<string, object> arguments);
}

/// <summary>
/// Result of a validation pass.
/// </summary>
public class ValidationPassResult
{
    /// <summary>
    /// Whether validation passed.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation issues found.
    /// </summary>
    public List<ValidationIssue> Issues { get; set; } = new();

    /// <summary>
    /// Sanitized output (if modifications were made).
    /// </summary>
    public string? SanitizedOutput { get; set; }

    /// <summary>
    /// Confidence score (0-1).
    /// </summary>
    public double Confidence { get; set; } = 1.0;
}

/// <summary>
/// A validation issue.
/// </summary>
public class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? Position { get; set; }
}

/// <summary>
/// Severity levels for validation issues.
/// </summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Context for validation.
/// </summary>
public class ValidationContext
{
    /// <summary>
    /// Original user query.
    /// </summary>
    public string? OriginalQuery { get; set; }

    /// <summary>
    /// Expected output type.
    /// </summary>
    public OutputType ExpectedType { get; set; } = OutputType.Text;

    /// <summary>
    /// Whether code execution is expected.
    /// </summary>
    public bool ExpectsCode { get; set; }

    /// <summary>
    /// Whether the output should contain file paths.
    /// </summary>
    public bool ExpectsFilePaths { get; set; }

    /// <summary>
    /// Additional validation rules to apply.
    /// </summary>
    public List<string> CustomRules { get; set; } = new();
}

/// <summary>
/// Expected output types.
/// </summary>
public enum OutputType
{
    Text,
    Code,
    Json,
    Markdown,
    ToolCall
}

/// <summary>
/// Validation implementation.
/// </summary>
public class ValidationPass : IValidationPass
{
    private readonly ILogger<ValidationPass> _logger;

    // Patterns that should never appear in output
    private static readonly Regex[] DangerousPatterns = new[]
    {
        new Regex(@"(?i)api[_-]?key\s*[=:]\s*['""][^'""]+['""]", RegexOptions.Compiled),
        new Regex(@"(?i)password\s*[=:]\s*['""][^'""]+['""]", RegexOptions.Compiled),
        new Regex(@"(?i)secret\s*[=:]\s*['""][^'""]+['""]", RegexOptions.Compiled),
        new Regex(@"(?i)token\s*[=:]\s*['""][^'""]+['""]", RegexOptions.Compiled),
        new Regex(@"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----", RegexOptions.Compiled),
        new Regex(@"(?i)bearer\s+[a-zA-Z0-9\-_.~+/]+=*", RegexOptions.Compiled),
    };

    // Patterns indicating hallucinated content
    private static readonly string[] HallucinationIndicators = new[]
    {
        "I cannot access",
        "I don't have access to",
        "I cannot browse",
        "I cannot execute",
        "As an AI",
        "I'm just an AI",
        "I apologize, but I cannot",
    };

    public ValidationPass(ILogger<ValidationPass> logger)
    {
        _logger = logger;
    }

    public ValidationPassResult Validate(string output, ValidationContext context)
    {
        var result = new ValidationPassResult
        {
            IsValid = true,
            SanitizedOutput = output
        };

        // Check for dangerous patterns (credentials, secrets)
        foreach (var pattern in DangerousPatterns)
        {
            if (pattern.IsMatch(output))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Critical,
                    Code = "SENSITIVE_DATA",
                    Message = "Output may contain sensitive data"
                });
                result.IsValid = false;
                result.SanitizedOutput = pattern.Replace(output, "[REDACTED]");
            }
        }

        // Check for hallucination indicators
        foreach (var indicator in HallucinationIndicators)
        {
            if (output.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Code = "POSSIBLE_HALLUCINATION",
                    Message = $"Output contains phrase that may indicate hallucination: '{indicator}'"
                });
                result.Confidence -= 0.2;
            }
        }

        // Validate output matches expected type
        ValidateOutputType(output, context, result);

        // Check for incomplete responses
        if (output.EndsWith("...", StringComparison.Ordinal) ||
            output.Contains("[truncated]") ||
            output.Contains("(continued)"))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Info,
                Code = "INCOMPLETE",
                Message = "Output appears to be truncated"
            });
        }

        // Check response addresses the query
        if (!string.IsNullOrEmpty(context.OriginalQuery))
        {
            ValidateRelevance(output, context.OriginalQuery, result);
        }

        result.Confidence = Math.Max(0, Math.Min(1, result.Confidence));

        _logger.LogDebug("Validation completed: Valid={IsValid}, Issues={IssueCount}, Confidence={Confidence}",
            result.IsValid, result.Issues.Count, result.Confidence);

        return result;
    }

    public ValidationResult ValidateToolCall(string toolName, Dictionary<string, object> arguments)
    {
        // Basic validation for common tools
        switch (toolName.ToLowerInvariant())
        {
            case "read_file":
            case "write_file":
            case "list_files":
                if (!arguments.ContainsKey("path"))
                {
                    return ValidationResult.Invalid($"Tool '{toolName}' requires 'path' argument");
                }
                break;

            case "run_command":
                if (!arguments.ContainsKey("command"))
                {
                    return ValidationResult.Invalid($"Tool '{toolName}' requires 'command' argument");
                }
                break;

            case "fetch":
                if (!arguments.ContainsKey("url"))
                {
                    return ValidationResult.Invalid($"Tool '{toolName}' requires 'url' argument");
                }
                break;
        }

        return ValidationResult.Valid();
    }

    private void ValidateOutputType(string output, ValidationContext context, ValidationPassResult result)
    {
        switch (context.ExpectedType)
        {
            case OutputType.Json:
                try
                {
                    System.Text.Json.JsonDocument.Parse(output);
                }
                catch
                {
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "INVALID_JSON",
                        Message = "Expected JSON output but received invalid JSON"
                    });
                    result.IsValid = false;
                }
                break;

            case OutputType.Code:
                if (!ContainsCodeBlock(output) && context.ExpectsCode)
                {
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Code = "MISSING_CODE",
                        Message = "Expected code in output but none found"
                    });
                }
                break;

            case OutputType.ToolCall:
                if (!output.Contains("{") || !output.Contains("tool"))
                {
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Code = "INVALID_TOOL_CALL",
                        Message = "Expected tool call format"
                    });
                    result.IsValid = false;
                }
                break;
        }
    }

    private void ValidateRelevance(string output, string query, ValidationPassResult result)
    {
        // Simple keyword overlap check
        var queryWords = ExtractKeywords(query);
        var outputWords = ExtractKeywords(output);

        var overlap = queryWords.Intersect(outputWords, StringComparer.OrdinalIgnoreCase).Count();
        var relevance = queryWords.Count > 0 ? (double)overlap / queryWords.Count : 1.0;

        if (relevance < 0.1 && output.Length > 100)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Code = "LOW_RELEVANCE",
                Message = "Output may not be relevant to the query"
            });
            result.Confidence -= 0.1;
        }
    }

    private static bool ContainsCodeBlock(string text)
    {
        return text.Contains("```") ||
               text.Contains("    ") || // Indented code
               Regex.IsMatch(text, @"^\s*(function|class|def|public|private|const|let|var)\s", RegexOptions.Multiline);
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\b[a-z]{3,}\b")
            .Select(m => m.Value)
            .Where(w => !IsStopWord(w));

        return new HashSet<string>(words);
    }

    private static bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>
        {
            "the", "and", "for", "that", "this", "with", "from", "have", "are",
            "was", "were", "been", "being", "has", "had", "does", "did", "will",
            "would", "could", "should", "can", "may", "might", "must"
        };
        return stopWords.Contains(word);
    }
}
