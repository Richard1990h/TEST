using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace LittleHelperAI.KingFactory.Context;

/// <summary>
/// Summarizes conversation context for compression.
/// </summary>
public interface IContextSummarizer
{
    /// <summary>
    /// Summarize a conversation.
    /// </summary>
    Task<ConversationSummary> SummarizeAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a compressed context from messages.
    /// </summary>
    Task<string> CreateCompressedContextAsync(IReadOnlyList<ChatMessage> messages, int maxTokens, CancellationToken cancellationToken = default);
}

/// <summary>
/// Summary of a conversation.
/// </summary>
public class ConversationSummary
{
    /// <summary>
    /// Main topic/goal of the conversation.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Key points discussed.
    /// </summary>
    public List<string> KeyPoints { get; set; } = new();

    /// <summary>
    /// Decisions made.
    /// </summary>
    public List<string> Decisions { get; set; } = new();

    /// <summary>
    /// Actions taken (tool usages, file changes, etc.).
    /// </summary>
    public List<string> Actions { get; set; } = new();

    /// <summary>
    /// Current state/context.
    /// </summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// Pending items or next steps.
    /// </summary>
    public List<string> PendingItems { get; set; } = new();

    /// <summary>
    /// Number of messages summarized.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// When summary was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Convert to a context string.
    /// </summary>
    public string ToContextString()
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Conversation Summary");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(Topic))
        {
            sb.AppendLine($"**Topic:** {Topic}");
            sb.AppendLine();
        }

        if (KeyPoints.Count > 0)
        {
            sb.AppendLine("**Key Points:**");
            foreach (var point in KeyPoints)
            {
                sb.AppendLine($"- {point}");
            }
            sb.AppendLine();
        }

        if (Decisions.Count > 0)
        {
            sb.AppendLine("**Decisions:**");
            foreach (var decision in Decisions)
            {
                sb.AppendLine($"- {decision}");
            }
            sb.AppendLine();
        }

        if (Actions.Count > 0)
        {
            sb.AppendLine("**Actions Taken:**");
            foreach (var action in Actions)
            {
                sb.AppendLine($"- {action}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(CurrentState))
        {
            sb.AppendLine($"**Current State:** {CurrentState}");
            sb.AppendLine();
        }

        if (PendingItems.Count > 0)
        {
            sb.AppendLine("**Pending:**");
            foreach (var item in PendingItems)
            {
                sb.AppendLine($"- {item}");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// LLM-based context summarizer.
/// </summary>
public class ContextSummarizer : IContextSummarizer
{
    private readonly ILogger<ContextSummarizer> _logger;
    private readonly ILlmEngine _llmEngine;
    private readonly IMessageWindowing _messageWindowing;

    public ContextSummarizer(
        ILogger<ContextSummarizer> logger,
        ILlmEngine llmEngine,
        IMessageWindowing messageWindowing)
    {
        _logger = logger;
        _llmEngine = llmEngine;
        _messageWindowing = messageWindowing;
    }

    public async Task<ConversationSummary> SummarizeAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Summarizing {Count} messages", messages.Count);

        var summary = new ConversationSummary { MessageCount = messages.Count };

        // For short conversations, extract info directly
        if (messages.Count <= 5)
        {
            ExtractDirectSummary(messages, summary);
            return summary;
        }

        // For longer conversations, use LLM
        var prompt = BuildSummarizationPrompt(messages);
        var response = await GenerateResponseAsync(prompt, cancellationToken);

        ParseSummaryResponse(response, summary);

        return summary;
    }

    public async Task<string> CreateCompressedContextAsync(IReadOnlyList<ChatMessage> messages, int maxTokens, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        var usedTokens = 0;

        // Include system messages in full
        var systemMessages = messages.Where(m => m.Role == "system").ToList();
        foreach (var msg in systemMessages)
        {
            var tokens = _messageWindowing.EstimateTokens(msg);
            if (usedTokens + tokens < maxTokens * 0.3) // Reserve 30% for system
            {
                sb.AppendLine($"[System]: {msg.Content}");
                sb.AppendLine();
                usedTokens += tokens;
            }
        }

        var nonSystemMessages = messages.Where(m => m.Role != "system").ToList();

        // If messages fit, include them
        var totalNonSystem = nonSystemMessages.Sum(m => _messageWindowing.EstimateTokens(m));
        if (usedTokens + totalNonSystem <= maxTokens)
        {
            foreach (var msg in nonSystemMessages)
            {
                sb.AppendLine($"[{char.ToUpper(msg.Role[0])}{msg.Role[1..]}]: {msg.Content}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // Otherwise, summarize older messages and include recent ones
        var recentCount = Math.Min(4, nonSystemMessages.Count);
        var olderMessages = nonSystemMessages.Take(nonSystemMessages.Count - recentCount).ToList();
        var recentMessages = nonSystemMessages.Skip(nonSystemMessages.Count - recentCount).ToList();

        if (olderMessages.Count > 0)
        {
            var summary = await SummarizeAsync(olderMessages, cancellationToken);
            sb.AppendLine("## Previous Context");
            sb.AppendLine(summary.ToContextString());
            sb.AppendLine();
        }

        sb.AppendLine("## Recent Messages");
        foreach (var msg in recentMessages)
        {
            sb.AppendLine($"[{char.ToUpper(msg.Role[0])}{msg.Role[1..]}]: {msg.Content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void ExtractDirectSummary(IReadOnlyList<ChatMessage> messages, ConversationSummary summary)
    {
        // Extract topic from first user message
        var firstUser = messages.FirstOrDefault(m => m.Role == "user");
        if (firstUser != null)
        {
            summary.Topic = TruncateText(firstUser.Content, 100);
        }

        // Extract tool usages as actions
        foreach (var msg in messages)
        {
            if (msg.ToolCalls != null)
            {
                foreach (var tool in msg.ToolCalls)
                {
                    summary.Actions.Add($"Used {tool.ToolName}");
                }
            }
        }

        // Last assistant message as current state
        var lastAssistant = messages.LastOrDefault(m => m.Role == "assistant");
        if (lastAssistant != null)
        {
            summary.CurrentState = TruncateText(lastAssistant.Content, 200);
        }
    }

    private string BuildSummarizationPrompt(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Summarize this conversation concisely. Extract:");
        sb.AppendLine("- TOPIC: Main subject (one line)");
        sb.AppendLine("- KEY_POINTS: Important points discussed (bullet list)");
        sb.AppendLine("- DECISIONS: Any decisions made (bullet list)");
        sb.AppendLine("- ACTIONS: Tools used or changes made (bullet list)");
        sb.AppendLine("- STATE: Current state/progress (one line)");
        sb.AppendLine("- PENDING: Unfinished items (bullet list)");
        sb.AppendLine();
        sb.AppendLine("## Conversation:");

        foreach (var msg in messages.TakeLast(20))
        {
            var content = TruncateText(msg.Content, 300);
            sb.AppendLine($"[{msg.Role}]: {content}");
        }

        return sb.ToString();
    }

    private async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = new StringBuilder();

        await foreach (var token in _llmEngine.StreamAsync(prompt, maxTokens: 500, cancellationToken: cancellationToken))
        {
            response.Append(token);
        }

        return response.ToString();
    }

    private void ParseSummaryResponse(string response, ConversationSummary summary)
    {
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var currentSection = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("TOPIC:", StringComparison.OrdinalIgnoreCase))
            {
                summary.Topic = trimmed.Substring("TOPIC:".Length).Trim();
            }
            else if (trimmed.StartsWith("KEY_POINTS:", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "key_points";
            }
            else if (trimmed.StartsWith("DECISIONS:", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "decisions";
            }
            else if (trimmed.StartsWith("ACTIONS:", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "actions";
            }
            else if (trimmed.StartsWith("STATE:", StringComparison.OrdinalIgnoreCase))
            {
                summary.CurrentState = trimmed.Substring("STATE:".Length).Trim();
                currentSection = "";
            }
            else if (trimmed.StartsWith("PENDING:", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "pending";
            }
            else if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
            {
                var item = trimmed.Substring(1).Trim();
                switch (currentSection)
                {
                    case "key_points":
                        summary.KeyPoints.Add(item);
                        break;
                    case "decisions":
                        summary.Decisions.Add(item);
                        break;
                    case "actions":
                        summary.Actions.Add(item);
                        break;
                    case "pending":
                        summary.PendingItems.Add(item);
                        break;
                }
            }
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }
}
