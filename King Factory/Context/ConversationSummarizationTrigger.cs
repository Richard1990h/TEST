using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Pipeline;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Context;

/// <summary>
/// Configuration for summarization triggers.
/// </summary>
public class SummarizationTriggerOptions
{
    /// <summary>
    /// Number of messages before triggering summarization.
    /// </summary>
    public int MessageThreshold { get; set; } = 20;

    /// <summary>
    /// Total token count before triggering summarization.
    /// </summary>
    public int TokenThreshold { get; set; } = 3000;

    /// <summary>
    /// Context window percentage to trigger at (e.g., 0.7 = 70%).
    /// </summary>
    public double ContextWindowThreshold { get; set; } = 0.7;

    /// <summary>
    /// Minimum messages to keep unsummarized.
    /// </summary>
    public int MinRecentMessages { get; set; } = 4;

    /// <summary>
    /// Whether to auto-summarize when thresholds are exceeded.
    /// </summary>
    public bool AutoSummarize { get; set; } = true;
}

/// <summary>
/// Result of summarization trigger check.
/// </summary>
public class SummarizationTriggerResult
{
    /// <summary>
    /// Whether summarization should be triggered.
    /// </summary>
    public bool ShouldSummarize { get; set; }

    /// <summary>
    /// Reason for the trigger.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Current message count.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Current estimated token count.
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// Context window utilization (0-1).
    /// </summary>
    public double ContextUtilization { get; set; }

    /// <summary>
    /// Number of messages to summarize (keep the rest).
    /// </summary>
    public int MessagesToSummarize { get; set; }

    public static SummarizationTriggerResult NoSummarizationNeeded(int messageCount, int tokens, double utilization) => new()
    {
        ShouldSummarize = false,
        MessageCount = messageCount,
        EstimatedTokens = tokens,
        ContextUtilization = utilization
    };

    public static SummarizationTriggerResult TriggerSummarization(
        string reason,
        int messageCount,
        int tokens,
        double utilization,
        int messagesToSummarize) => new()
    {
        ShouldSummarize = true,
        Reason = reason,
        MessageCount = messageCount,
        EstimatedTokens = tokens,
        ContextUtilization = utilization,
        MessagesToSummarize = messagesToSummarize
    };
}

/// <summary>
/// Determines when conversation summarization should be triggered.
/// </summary>
public interface IConversationSummarizationTrigger
{
    /// <summary>
    /// Check if summarization should be triggered.
    /// </summary>
    SummarizationTriggerResult Check(IReadOnlyList<ChatMessage> messages, int maxContextTokens);

    /// <summary>
    /// Check if summarization should be triggered for a conversation buffer.
    /// </summary>
    SummarizationTriggerResult Check(IConversationBuffer conversation, int maxContextTokens);
}

/// <summary>
/// Implementation of summarization trigger.
/// </summary>
public class ConversationSummarizationTrigger : IConversationSummarizationTrigger
{
    private readonly ILogger<ConversationSummarizationTrigger> _logger;
    private readonly IMessageWindowing _messageWindowing;
    private readonly SummarizationTriggerOptions _options;

    public ConversationSummarizationTrigger(
        ILogger<ConversationSummarizationTrigger> logger,
        IMessageWindowing messageWindowing)
    {
        _logger = logger;
        _messageWindowing = messageWindowing;
        _options = new SummarizationTriggerOptions();
    }

    public ConversationSummarizationTrigger(
        ILogger<ConversationSummarizationTrigger> logger,
        IMessageWindowing messageWindowing,
        SummarizationTriggerOptions options)
    {
        _logger = logger;
        _messageWindowing = messageWindowing;
        _options = options;
    }

    public SummarizationTriggerResult Check(IReadOnlyList<ChatMessage> messages, int maxContextTokens)
    {
        if (!_options.AutoSummarize)
        {
            return SummarizationTriggerResult.NoSummarizationNeeded(messages.Count, 0, 0);
        }

        var messageCount = messages.Count;
        var estimatedTokens = messages.Sum(m => _messageWindowing.EstimateTokens(m));
        var contextUtilization = maxContextTokens > 0 ? (double)estimatedTokens / maxContextTokens : 0;

        // Check message threshold
        if (messageCount >= _options.MessageThreshold)
        {
            var messagesToSummarize = messageCount - _options.MinRecentMessages;
            if (messagesToSummarize > 0)
            {
                _logger.LogInformation(
                    "Summarization triggered: message count {Count} >= threshold {Threshold}",
                    messageCount,
                    _options.MessageThreshold);

                return SummarizationTriggerResult.TriggerSummarization(
                    $"Message count ({messageCount}) exceeded threshold ({_options.MessageThreshold})",
                    messageCount,
                    estimatedTokens,
                    contextUtilization,
                    messagesToSummarize);
            }
        }

        // Check token threshold
        if (estimatedTokens >= _options.TokenThreshold)
        {
            var messagesToSummarize = CalculateMessagesToSummarize(messages, _options.TokenThreshold / 2);
            if (messagesToSummarize > 0)
            {
                _logger.LogInformation(
                    "Summarization triggered: token count {Tokens} >= threshold {Threshold}",
                    estimatedTokens,
                    _options.TokenThreshold);

                return SummarizationTriggerResult.TriggerSummarization(
                    $"Token count ({estimatedTokens}) exceeded threshold ({_options.TokenThreshold})",
                    messageCount,
                    estimatedTokens,
                    contextUtilization,
                    messagesToSummarize);
            }
        }

        // Check context window utilization
        if (contextUtilization >= _options.ContextWindowThreshold)
        {
            var targetTokens = (int)(maxContextTokens * 0.5); // Aim to get to 50% utilization
            var messagesToSummarize = CalculateMessagesToSummarize(messages, targetTokens);
            if (messagesToSummarize > 0)
            {
                _logger.LogInformation(
                    "Summarization triggered: context utilization {Utilization:P0} >= threshold {Threshold:P0}",
                    contextUtilization,
                    _options.ContextWindowThreshold);

                return SummarizationTriggerResult.TriggerSummarization(
                    $"Context utilization ({contextUtilization:P0}) exceeded threshold ({_options.ContextWindowThreshold:P0})",
                    messageCount,
                    estimatedTokens,
                    contextUtilization,
                    messagesToSummarize);
            }
        }

        return SummarizationTriggerResult.NoSummarizationNeeded(messageCount, estimatedTokens, contextUtilization);
    }

    public SummarizationTriggerResult Check(IConversationBuffer conversation, int maxContextTokens)
    {
        return Check(conversation.GetMessages(), maxContextTokens);
    }

    private int CalculateMessagesToSummarize(IReadOnlyList<ChatMessage> messages, int targetTokens)
    {
        // Keep the most recent messages unsummarized
        var keepCount = _options.MinRecentMessages;
        var toConsider = messages.Count - keepCount;

        if (toConsider <= 0)
            return 0;

        // Count tokens from the oldest messages until we reach the target
        var tokenCount = 0;
        var summarizeCount = 0;

        for (int i = 0; i < toConsider; i++)
        {
            var tokens = _messageWindowing.EstimateTokens(messages[i]);
            if (tokenCount + tokens > targetTokens)
                break;

            tokenCount += tokens;
            summarizeCount++;
        }

        return summarizeCount;
    }
}

/// <summary>
/// Extension methods for conversation summarization.
/// </summary>
public static class ConversationSummarizationExtensions
{
    /// <summary>
    /// Apply summarization to a conversation if triggered.
    /// </summary>
    public static async Task<bool> ApplySummarizationIfNeededAsync(
        this IConversationBuffer conversation,
        IConversationSummarizationTrigger trigger,
        IContextSummarizer summarizer,
        int maxContextTokens,
        CancellationToken cancellationToken = default)
    {
        var messages = conversation.GetMessages();
        var result = trigger.Check(messages, maxContextTokens);

        if (!result.ShouldSummarize || result.MessagesToSummarize <= 0)
            return false;

        // Get messages to summarize
        var toSummarize = messages.Take(result.MessagesToSummarize).ToList();
        var toKeep = messages.Skip(result.MessagesToSummarize).ToList();

        // Generate summary
        var summary = await summarizer.SummarizeAsync(toSummarize, cancellationToken);

        // Create a new conversation with summary + recent messages
        conversation.Clear();

        // Add summary as a system message
        conversation.AddMessage(new ChatMessage
        {
            Role = "system",
            Content = $"[Previous conversation summary]\n{summary.ToContextString()}"
        });

        // Add recent messages back
        foreach (var message in toKeep)
        {
            conversation.AddMessage(message);
        }

        return true;
    }
}
