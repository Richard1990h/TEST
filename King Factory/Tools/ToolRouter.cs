using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace LittleHelperAI.KingFactory.Tools;

/// <summary>
/// Routes tool calls to the appropriate tool implementation.
/// </summary>
public interface IToolRouter
{
    /// <summary>
    /// Execute a tool call.
    /// </summary>
    Task<ToolResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse a tool call from LLM output.
    /// </summary>
    ToolCall? ParseToolCall(string llmOutput);

    /// <summary>
    /// Check if a string contains a tool call.
    /// </summary>
    bool ContainsToolCall(string text);
}

/// <summary>
/// Implementation of tool router.
/// </summary>
public class ToolRouter : IToolRouter
{
    private readonly IToolRegistry _registry;
    private readonly ILogger<ToolRouter> _logger;

    public ToolRouter(IToolRegistry registry, ILogger<ToolRouter> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Executing tool: {ToolName}", toolCall.ToolName);

        var tool = _registry.GetTool(toolCall.ToolName);
        if (tool == null)
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.ToolName,
                Success = false,
                Error = $"Unknown tool: {toolCall.ToolName}",
                ExecutionTime = stopwatch.Elapsed
            };
        }

        // Validate arguments
        var validation = tool.ValidateArguments(toolCall.Arguments);
        if (!validation.IsValid)
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.ToolName,
                Success = false,
                Error = $"Invalid arguments: {string.Join(", ", validation.Errors)}",
                ExecutionTime = stopwatch.Elapsed
            };
        }

        try
        {
            var result = await tool.ExecuteAsync(toolCall.Arguments, cancellationToken);
            result.ToolCallId = toolCall.Id;
            result.ExecutionTime = stopwatch.Elapsed;

            _logger.LogInformation("Tool {ToolName} completed in {Time}ms: {Success}",
                toolCall.ToolName, stopwatch.ElapsedMilliseconds, result.Success);

            return result;
        }
        catch (OperationCanceledException)
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.ToolName,
                Success = false,
                Error = "Operation cancelled",
                ExecutionTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} failed", toolCall.ToolName);

            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                ToolName = toolCall.ToolName,
                Success = false,
                Error = ex.Message,
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }

    public bool ContainsToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check for tool code block
        return text.Contains("```tool") || text.Contains("```json") && text.Contains("\"tool\"");
    }

    public ToolCall? ParseToolCall(string llmOutput)
    {
        if (string.IsNullOrWhiteSpace(llmOutput))
            return null;

        try
        {
            // Extract JSON from code block
            var json = ExtractToolJson(llmOutput);
            if (string.IsNullOrEmpty(json))
                return null;

            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tool", out var toolProp))
                return null;

            var toolName = toolProp.GetString();
            if (string.IsNullOrEmpty(toolName))
                return null;

            var arguments = new Dictionary<string, object>();

            if (root.TryGetProperty("arguments", out var argsProp))
            {
                foreach (var prop in argsProp.EnumerateObject())
                {
                    arguments[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Array => prop.Value.ToString(),
                        JsonValueKind.Object => prop.Value.ToString(),
                        _ => prop.Value.ToString()
                    };
                }
            }

            return new ToolCall
            {
                ToolName = toolName,
                Arguments = arguments
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse tool call");
            return null;
        }
    }

    private static string? ExtractToolJson(string text)
    {
        // Try to extract from ```tool block
        var toolStart = text.IndexOf("```tool");
        if (toolStart >= 0)
        {
            var jsonStart = text.IndexOf('\n', toolStart) + 1;
            var jsonEnd = text.IndexOf("```", jsonStart);
            if (jsonEnd > jsonStart)
            {
                return text.Substring(jsonStart, jsonEnd - jsonStart).Trim();
            }
        }

        // Try to extract from ```json block containing "tool"
        var jsonBlockStart = text.IndexOf("```json");
        if (jsonBlockStart >= 0)
        {
            var start = text.IndexOf('\n', jsonBlockStart) + 1;
            var end = text.IndexOf("```", start);
            if (end > start)
            {
                var json = text.Substring(start, end - start).Trim();
                if (json.Contains("\"tool\""))
                    return json;
            }
        }

        // Try to find raw JSON object with "tool" property
        var braceStart = text.IndexOf('{');
        if (braceStart >= 0)
        {
            var depth = 0;
            for (int i = braceStart; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}') depth--;

                if (depth == 0)
                {
                    var json = text.Substring(braceStart, i - braceStart + 1);
                    if (json.Contains("\"tool\""))
                        return json;
                    break;
                }
            }
        }

        return null;
    }
}
