using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Context;

/// <summary>
/// Manages message context windows for LLM.
/// </summary>
public interface IMessageWindowing
{
    /// <summary>
    /// Get messages that fit within token budget.
    /// </summary>
    IReadOnlyList<ChatMessage> GetWindow(IReadOnlyList<ChatMessage> messages, int maxTokens);

    /// <summary>
    /// Get messages with importance-based selection.
    /// </summary>
    IReadOnlyList<ChatMessage> GetImportantWindow(IReadOnlyList<ChatMessage> messages, int maxTokens);

    /// <summary>
    /// Estimate token count for a message.
    /// </summary>
    int EstimateTokens(ChatMessage message);

    /// <summary>
    /// Estimate token count for text.
    /// </summary>
    int EstimateTokens(string text);
}

/// <summary>
/// Token-aware message windowing implementation.
/// </summary>
public class MessageWindowing : IMessageWindowing
{
    private readonly ILogger<MessageWindowing> _logger;
    private readonly double _charsPerToken;

    public MessageWindowing(ILogger<MessageWindowing> logger, double charsPerToken = 4.0)
    {
        _logger = logger;
        _charsPerToken = charsPerToken;
    }

    public IReadOnlyList<ChatMessage> GetWindow(IReadOnlyList<ChatMessage> messages, int maxTokens)
    {
        var result = new List<ChatMessage>();
        var tokenCount = 0;

        // Always include system messages first
        var systemMessages = messages.Where(m => m.Role == "system").ToList();
        foreach (var msg in systemMessages)
        {
            var msgTokens = EstimateTokens(msg);
            tokenCount += msgTokens;
            result.Add(msg);
        }

        // Add non-system messages from newest to oldest
        var otherMessages = messages
            .Where(m => m.Role != "system")
            .Reverse()
            .ToList();

        var messagesToAdd = new List<ChatMessage>();

        foreach (var msg in otherMessages)
        {
            var msgTokens = EstimateTokens(msg);
            if (tokenCount + msgTokens > maxTokens)
                break;

            messagesToAdd.Add(msg);
            tokenCount += msgTokens;
        }

        // Reverse to maintain chronological order
        messagesToAdd.Reverse();
        result.AddRange(messagesToAdd);

        _logger.LogDebug("Windowed {Selected}/{Total} messages, ~{Tokens} tokens",
            result.Count, messages.Count, tokenCount);

        return result.AsReadOnly();
    }

    public IReadOnlyList<ChatMessage> GetImportantWindow(IReadOnlyList<ChatMessage> messages, int maxTokens)
    {
        // Score each message by importance
        var scored = messages
            .Select(m => (Message: m, Score: CalculateImportance(m, messages)))
            .ToList();

        var result = new List<ChatMessage>();
        var tokenCount = 0;

        // Always include system messages
        foreach (var (msg, _) in scored.Where(s => s.Message.Role == "system"))
        {
            var msgTokens = EstimateTokens(msg);
            tokenCount += msgTokens;
            result.Add(msg);
        }

        // Include first user message (sets context)
        var firstUser = messages.FirstOrDefault(m => m.Role == "user");
        if (firstUser != null && !result.Contains(firstUser))
        {
            var tokens = EstimateTokens(firstUser);
            if (tokenCount + tokens <= maxTokens)
            {
                result.Add(firstUser);
                tokenCount += tokens;
            }
        }

        // Include last few messages for continuity
        var recent = messages
            .Where(m => m.Role != "system")
            .TakeLast(4)
            .ToList();

        foreach (var msg in recent)
        {
            if (result.Contains(msg))
                continue;

            var tokens = EstimateTokens(msg);
            if (tokenCount + tokens <= maxTokens)
            {
                result.Add(msg);
                tokenCount += tokens;
            }
        }

        // Fill remaining space with high-importance messages
        var remaining = scored
            .Where(s => !result.Contains(s.Message) && s.Message.Role != "system")
            .OrderByDescending(s => s.Score)
            .ToList();

        foreach (var (msg, _) in remaining)
        {
            var tokens = EstimateTokens(msg);
            if (tokenCount + tokens > maxTokens)
                continue;

            result.Add(msg);
            tokenCount += tokens;
        }

        // Sort by original order
        var messageOrder = messages.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
        result.Sort((a, b) => messageOrder.GetValueOrDefault(a, 0).CompareTo(messageOrder.GetValueOrDefault(b, 0)));

        _logger.LogDebug("Important window: {Selected}/{Total} messages, ~{Tokens} tokens",
            result.Count, messages.Count, tokenCount);

        return result.AsReadOnly();
    }

    public int EstimateTokens(ChatMessage message)
    {
        var tokens = EstimateTokens(message.Content);

        // Add overhead for role and structure
        tokens += 4; // Role marker

        // Tool calls add tokens
        if (message.ToolCalls != null)
        {
            foreach (var tool in message.ToolCalls)
            {
                tokens += EstimateTokens(tool.ToolName);
                tokens += EstimateTokens(System.Text.Json.JsonSerializer.Serialize(tool.Arguments));
            }
        }

        return tokens;
    }

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Rough estimate: ~4 characters per token for English
        return (int)Math.Ceiling(text.Length / _charsPerToken);
    }

    private double CalculateImportance(ChatMessage message, IReadOnlyList<ChatMessage> allMessages)
    {
        var score = 0.0;
        var index = allMessages.ToList().IndexOf(message);
        var totalMessages = allMessages.Count;

        // Recency bonus (0-0.3)
        score += 0.3 * (index / (double)Math.Max(1, totalMessages - 1));

        // Role-based scoring
        score += message.Role switch
        {
            "system" => 1.0,
            "user" => 0.5,
            "assistant" => 0.3,
            "tool" => 0.2,
            _ => 0.1
        };

        // Content-based scoring
        var content = message.Content.ToLowerInvariant();

        // Questions are important
        if (content.Contains("?"))
            score += 0.2;

        // Tool usage is important
        if (message.ToolCalls?.Count > 0)
            score += 0.3;

        // Code blocks are important
        if (content.Contains("```"))
            score += 0.2;

        // Error messages are important
        if (content.Contains("error") || content.Contains("exception") || content.Contains("failed"))
            score += 0.3;

        // File references are important
        if (content.Contains(".cs") || content.Contains(".js") || content.Contains(".py"))
            score += 0.1;

        return Math.Min(2.0, score);
    }
}
