using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace LittleHelperAI.KingFactory.Tools;

/// <summary>
/// Configuration for tool result sanitization.
/// </summary>
public class ToolSanitizationOptions
{
    /// <summary>
    /// Maximum characters for tool output.
    /// </summary>
    public int MaxOutputLength { get; set; } = 8000;

    /// <summary>
    /// Maximum characters for file content in read_file results.
    /// </summary>
    public int MaxFileContentLength { get; set; } = 10000;

    /// <summary>
    /// Maximum lines to include from file content.
    /// </summary>
    public int MaxFileLines { get; set; } = 200;

    /// <summary>
    /// Maximum characters for command output.
    /// </summary>
    public int MaxCommandOutputLength { get; set; } = 5000;

    /// <summary>
    /// Whether to include truncation indicators.
    /// </summary>
    public bool IncludeTruncationIndicator { get; set; } = true;

    /// <summary>
    /// Characters to show at beginning when truncating.
    /// </summary>
    public int TruncationHeadChars { get; set; } = 3000;

    /// <summary>
    /// Characters to show at end when truncating.
    /// </summary>
    public int TruncationTailChars { get; set; } = 1000;
}

/// <summary>
/// Sanitizes tool results to prevent oversized outputs.
/// </summary>
public interface IToolResultSanitizer
{
    /// <summary>
    /// Sanitize a tool result.
    /// </summary>
    ToolResult Sanitize(ToolResult result);

    /// <summary>
    /// Sanitize raw output string.
    /// </summary>
    string SanitizeOutput(string output, string? toolName = null);
}

/// <summary>
/// Implementation of tool result sanitizer.
/// </summary>
public class ToolResultSanitizer : IToolResultSanitizer
{
    private readonly ILogger<ToolResultSanitizer> _logger;
    private readonly ToolSanitizationOptions _options;

    // Patterns for sensitive data that should be redacted
    private static readonly Regex[] SensitivePatterns = new[]
    {
        new Regex(@"(?i)api[_-]?key\s*[=:]\s*['""]?[a-zA-Z0-9\-_.]+['""]?", RegexOptions.Compiled),
        new Regex(@"(?i)password\s*[=:]\s*['""]?[^\s'""]+['""]?", RegexOptions.Compiled),
        new Regex(@"(?i)secret\s*[=:]\s*['""]?[^\s'""]+['""]?", RegexOptions.Compiled),
        new Regex(@"(?i)token\s*[=:]\s*['""]?[a-zA-Z0-9\-_.]+['""]?", RegexOptions.Compiled),
        new Regex(@"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----[\s\S]*?-----END\s+(RSA\s+)?PRIVATE\s+KEY-----", RegexOptions.Compiled),
        new Regex(@"(?i)bearer\s+[a-zA-Z0-9\-_.~+/]+=*", RegexOptions.Compiled),
    };

    public ToolResultSanitizer(ILogger<ToolResultSanitizer> logger)
    {
        _logger = logger;
        _options = new ToolSanitizationOptions();
    }

    public ToolResultSanitizer(ILogger<ToolResultSanitizer> logger, ToolSanitizationOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public ToolResult Sanitize(ToolResult result)
    {
        if (string.IsNullOrEmpty(result.Output))
            return result;

        var originalLength = result.Output.Length;
        var sanitizedOutput = SanitizeOutput(result.Output, result.ToolName);

        if (sanitizedOutput.Length < originalLength)
        {
            _logger.LogInformation(
                "Sanitized tool result for {ToolName}: {OriginalLength} -> {SanitizedLength} chars",
                result.ToolName,
                originalLength,
                sanitizedOutput.Length);

            return new ToolResult
            {
                ToolCallId = result.ToolCallId,
                ToolName = result.ToolName,
                Success = result.Success,
                Output = sanitizedOutput,
                Error = result.Error,
                ExecutionTime = result.ExecutionTime
            };
        }

        return result;
    }

    public string SanitizeOutput(string output, string? toolName = null)
    {
        if (string.IsNullOrEmpty(output))
            return output;

        var result = output;

        // Redact sensitive data
        result = RedactSensitiveData(result);

        // Apply tool-specific limits
        var maxLength = GetMaxLengthForTool(toolName);

        // Truncate if needed
        if (result.Length > maxLength)
        {
            result = TruncateOutput(result, maxLength, toolName);
        }

        return result;
    }

    private string RedactSensitiveData(string output)
    {
        var result = output;

        foreach (var pattern in SensitivePatterns)
        {
            result = pattern.Replace(result, "[REDACTED]");
        }

        return result;
    }

    private int GetMaxLengthForTool(string? toolName)
    {
        return toolName?.ToLowerInvariant() switch
        {
            "read_file" => _options.MaxFileContentLength,
            "run_command" => _options.MaxCommandOutputLength,
            "list_files" => _options.MaxOutputLength,
            "fetch" => _options.MaxOutputLength,
            _ => _options.MaxOutputLength
        };
    }

    private string TruncateOutput(string output, int maxLength, string? toolName)
    {
        if (output.Length <= maxLength)
            return output;

        var sb = new StringBuilder();

        // For file content, try to truncate at line boundaries
        if (toolName?.ToLowerInvariant() == "read_file")
        {
            return TruncateFileContent(output);
        }

        // For command output, try to preserve structure
        if (toolName?.ToLowerInvariant() == "run_command")
        {
            return TruncateCommandOutput(output, maxLength);
        }

        // Standard truncation with head and tail
        return StandardTruncate(output, maxLength);
    }

    private string TruncateFileContent(string content)
    {
        var lines = content.Split('\n');

        if (lines.Length <= _options.MaxFileLines)
        {
            // Just truncate by character length
            return StandardTruncate(content, _options.MaxFileContentLength);
        }

        var sb = new StringBuilder();
        var headLines = _options.MaxFileLines * 2 / 3;
        var tailLines = _options.MaxFileLines - headLines - 1;

        // Add head lines
        for (int i = 0; i < headLines && i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length > 500)
                line = line.Substring(0, 500) + "...";
            sb.AppendLine(line);
        }

        // Add truncation indicator
        if (_options.IncludeTruncationIndicator)
        {
            var skipped = lines.Length - headLines - tailLines;
            sb.AppendLine();
            sb.AppendLine($"[... {skipped} lines truncated ...]");
            sb.AppendLine();
        }

        // Add tail lines
        for (int i = lines.Length - tailLines; i < lines.Length; i++)
        {
            if (i >= 0)
            {
                var line = lines[i];
                if (line.Length > 500)
                    line = line.Substring(0, 500) + "...";
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private string TruncateCommandOutput(string output, int maxLength)
    {
        // For command output, prioritize the end (often contains errors/results)
        var tailChars = Math.Min(_options.TruncationTailChars * 2, maxLength * 2 / 3);
        var headChars = maxLength - tailChars;

        if (output.Length <= maxLength)
            return output;

        var sb = new StringBuilder();

        if (headChars > 0)
        {
            sb.Append(output.Substring(0, headChars));
        }

        if (_options.IncludeTruncationIndicator)
        {
            var skipped = output.Length - headChars - tailChars;
            sb.AppendLine();
            sb.AppendLine($"[... {skipped} characters truncated ...]");
            sb.AppendLine();
        }

        if (tailChars > 0)
        {
            sb.Append(output.Substring(output.Length - tailChars));
        }

        return sb.ToString();
    }

    private string StandardTruncate(string output, int maxLength)
    {
        if (output.Length <= maxLength)
            return output;

        var indicatorLength = _options.IncludeTruncationIndicator ? 50 : 0;
        var available = maxLength - indicatorLength;
        var headChars = Math.Min(_options.TruncationHeadChars, available * 2 / 3);
        var tailChars = available - headChars;

        var sb = new StringBuilder();

        sb.Append(output.Substring(0, headChars));

        if (_options.IncludeTruncationIndicator)
        {
            var skipped = output.Length - headChars - tailChars;
            sb.AppendLine();
            sb.AppendLine($"[... {skipped} characters truncated ...]");
            sb.AppendLine();
        }

        sb.Append(output.Substring(output.Length - tailChars));

        return sb.ToString();
    }
}
