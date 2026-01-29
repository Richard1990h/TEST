using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Prompts;
using LittleHelperAI.KingFactory.Tools;
using System.Text;

namespace LittleHelperAI.KingFactory.Pipeline;

/// <summary>
/// Builds prompts for the LLM from conversation and context.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Build a complete prompt from messages and context.
    /// </summary>
    string BuildPrompt(IReadOnlyList<ChatMessage> messages, PromptContext? context = null);

    /// <summary>
    /// Build a prompt for planning mode.
    /// </summary>
    string BuildPlanningPrompt(string task, IReadOnlyList<ChatMessage> history);

    /// <summary>
    /// Build a prompt for tool selection.
    /// </summary>
    string BuildToolPrompt(string query, IReadOnlyList<ToolSchema> availableTools);

    /// <summary>
    /// Build a prompt for validation.
    /// </summary>
    string BuildValidationPrompt(string response, string originalQuery);
}

/// <summary>
/// Additional context for prompt building.
/// </summary>
public class PromptContext
{
    /// <summary>
    /// Whether the AI is in planning mode.
    /// </summary>
    public bool PlanningMode { get; set; }

    /// <summary>
    /// Current project/working directory context.
    /// </summary>
    public string? ProjectContext { get; set; }

    /// <summary>
    /// Available tools descriptions.
    /// </summary>
    public string? ToolsContext { get; set; }

    /// <summary>
    /// Any additional context to include.
    /// </summary>
    public string? AdditionalContext { get; set; }

    /// <summary>
    /// Developer prompt content inserted after system prompt.
    /// </summary>
    public string? DeveloperPrompt { get; set; }

    /// <summary>
    /// Override for the system prompt content.
    /// </summary>
    public string? SystemPromptOverride { get; set; }

    /// <summary>
    /// Suppress automatic tool descriptions even when tools are enabled.
    /// </summary>
    public bool SuppressToolDescriptions { get; set; }

    /// <summary>
    /// Task lock instruction to keep the model on-task.
    /// </summary>
    public string? TaskLock { get; set; }

    /// <summary>
    /// Enable strict code-only mode.
    /// </summary>
    public bool CodeMode { get; set; }

    /// <summary>
    /// Enable strict fix-only mode.
    /// </summary>
    public bool FixMode { get; set; }

    /// <summary>
    /// Current date/time for time-sensitive queries.
    /// </summary>
    public DateTime CurrentTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Builds prompts using the system prompt templates.
/// </summary>
public class PromptBuilder : IPromptBuilder
{
    private readonly ISystemPrompts _systemPrompts;
    private readonly IToolRegistry _toolRegistry;

    public PromptBuilder(ISystemPrompts systemPrompts, IToolRegistry toolRegistry)
    {
        _systemPrompts = systemPrompts;
        _toolRegistry = toolRegistry;
    }

    public string BuildPrompt(IReadOnlyList<ChatMessage> messages, PromptContext? context = null)
    {
        var sb = new StringBuilder();

        // Build system prompt content
        var systemContent = new StringBuilder();

        var includeTools = context?.ToolsContext != null;
        if (!string.IsNullOrWhiteSpace(context?.SystemPromptOverride))
        {
            systemContent.AppendLine(context.SystemPromptOverride.Trim());
        }
        else
        {
            var systemPrompt = _systemPrompts.Build(new PromptBuildOptions
            {
                IncludeTools = includeTools,
                IncludePlanning = context?.PlanningMode ?? false,
                IncludeCodeMode = context?.CodeMode ?? false,
                IncludeFixMode = context?.FixMode ?? false
            });
            systemContent.AppendLine(systemPrompt);
        }

        // Add project context if available
        if (!string.IsNullOrEmpty(context?.ProjectContext))
        {
            systemContent.AppendLine();
            systemContent.AppendLine("## Current Project Context");
            systemContent.AppendLine(context.ProjectContext);
        }

        // Add tools context
        if (includeTools && !(context?.SuppressToolDescriptions ?? false))
        {
            systemContent.AppendLine();
            systemContent.AppendLine(BuildToolsDescription());
        }

        // Add additional context
        if (!string.IsNullOrEmpty(context?.AdditionalContext))
        {
            systemContent.AppendLine();
            systemContent.AppendLine(context.AdditionalContext);
        }

        // Use ChatML format (Qwen/Llama compatible)
        sb.Append("<|im_start|>system\n");
        sb.Append(systemContent.ToString().Trim());
        sb.Append("<|im_end|>\n");

        if (!string.IsNullOrWhiteSpace(context?.DeveloperPrompt))
        {
            sb.Append("<|im_start|>system\n");
            sb.Append(context.DeveloperPrompt.Trim());
            sb.Append("<|im_end|>\n");
        }

        // Task lock must come immediately before the user message
        if (!string.IsNullOrWhiteSpace(context?.TaskLock))
        {
            sb.Append("<|im_start|>system\n");
            sb.Append(context.TaskLock.Trim());
            sb.Append("<|im_end|>\n");
        }

        // Add conversation history in ChatML format
        foreach (var message in messages.Where(m => m.Role != "system"))
        {
            var role = message.Role switch
            {
                "user" => "user",
                "assistant" => "assistant",
                "tool" => "user",  // Tool results go as user messages
                _ => message.Role
            };

            sb.Append($"<|im_start|>{role}\n");

            if (message.Role == "tool")
            {
                sb.Append($"[Tool Result]\n{message.Content}");
            }
            else
            {
                sb.Append(message.Content);
            }

            // Include tool calls if present
            if (message.ToolCalls != null && message.ToolCalls.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Tool Calls:");
                foreach (var call in message.ToolCalls)
                {
                    sb.AppendLine($"- {call.ToolName}({FormatArguments(call.Arguments)})");
                }
            }

            sb.Append("<|im_end|>\n");
        }

        // Start assistant turn
        sb.Append("<|im_start|>assistant\n");

        return sb.ToString();
    }

    public string BuildPlanningPrompt(string task, IReadOnlyList<ChatMessage> history)
    {
        var sb = new StringBuilder();

        sb.AppendLine(_systemPrompts.GetPlanningPrompt());
        sb.AppendLine();
        sb.AppendLine("## Task to Plan");
        sb.AppendLine(task);

        if (history.Any())
        {
            sb.AppendLine();
            sb.AppendLine("## Relevant Context");
            foreach (var msg in history.TakeLast(5))
            {
                sb.AppendLine($"{msg.Role}: {msg.Content.Substring(0, Math.Min(200, msg.Content.Length))}...");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Create a detailed plan with numbered steps:");

        return sb.ToString();
    }

    public string BuildToolPrompt(string query, IReadOnlyList<ToolSchema> availableTools)
    {
        var sb = new StringBuilder();

        sb.AppendLine(_systemPrompts.GetToolsPrompt());
        sb.AppendLine();
        sb.AppendLine("## Available Tools");

        foreach (var tool in availableTools)
        {
            sb.AppendLine($"- **{tool.Properties.Keys.FirstOrDefault()}**: Tool for {string.Join(", ", tool.Properties.Keys)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Query");
        sb.AppendLine(query);
        sb.AppendLine();
        sb.AppendLine("Select the appropriate tool and provide the arguments as JSON:");

        return sb.ToString();
    }

    public string BuildValidationPrompt(string response, string originalQuery)
    {
        var sb = new StringBuilder();

        sb.AppendLine(_systemPrompts.GetValidationPrompt());
        sb.AppendLine();
        sb.AppendLine("## Original Query");
        sb.AppendLine(originalQuery);
        sb.AppendLine();
        sb.AppendLine("## Response to Validate");
        sb.AppendLine(response);
        sb.AppendLine();
        sb.AppendLine("Validate this response and identify any issues:");

        return sb.ToString();
    }

    private string BuildToolsDescription()
    {
        var sb = new StringBuilder();
        var tools = _toolRegistry.GetAllTools();

        foreach (var tool in tools)
        {
            sb.AppendLine($"### {tool.Name}");
            sb.AppendLine(tool.Description);
            sb.AppendLine();
            sb.AppendLine("Parameters:");

            foreach (var param in tool.Schema.Properties)
            {
                var required = tool.Schema.Required?.Contains(param.Key) ?? false;
                sb.AppendLine($"- `{param.Key}` ({param.Value.Type}){(required ? " [required]" : "")}: {param.Value.Description}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatArguments(Dictionary<string, object> args)
    {
        return string.Join(", ", args.Select(a => $"{a.Key}={a.Value}"));
    }
}
