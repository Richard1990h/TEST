using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Intent;

/// <summary>
/// Classifies user intent from messages.
/// </summary>
public interface IIntentClassifier
{
    /// <summary>
    /// Classify the intent of a message.
    /// </summary>
    Task<IntentResult> ClassifyAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify with conversation context.
    /// </summary>
    Task<IntentResult> ClassifyWithContextAsync(string message, IReadOnlyList<ChatMessage> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pattern-based intent classifier.
/// </summary>
public class IntentClassifier : IIntentClassifier
{
    private readonly ILogger<IntentClassifier> _logger;
    private readonly List<IntentPattern> _patterns;

    public IntentClassifier(ILogger<IntentClassifier> logger)
    {
        _logger = logger;
        _patterns = InitializePatterns();
    }

    public Task<IntentResult> ClassifyAsync(string message, CancellationToken cancellationToken = default)
    {
        return ClassifyWithContextAsync(message, Array.Empty<ChatMessage>(), cancellationToken);
    }

    public Task<IntentResult> ClassifyWithContextAsync(string message, IReadOnlyList<ChatMessage> context, CancellationToken cancellationToken = default)
    {
        var normalizedMessage = message.ToLowerInvariant().Trim();
        var result = new IntentResult
        {
            OriginalMessage = message,
            Confidence = 0.0
        };

        // Check each pattern
        foreach (var pattern in _patterns)
        {
            var match = pattern.Match(normalizedMessage);
            if (match.IsMatch && match.Confidence > result.Confidence)
            {
                result.Intent = pattern.Intent;
                result.Category = pattern.Category;
                result.Confidence = match.Confidence;
                result.ExtractedEntities = match.Entities;
            }
        }

        // Default to general query if no strong match
        if (result.Confidence < 0.3)
        {
            result.Intent = IntentType.GeneralQuery;
            result.Category = IntentCategory.Information;
            result.Confidence = 0.5;
        }

        // Adjust based on context
        if (context.Any())
        {
            AdjustForContext(result, context);
        }

        _logger.LogDebug("Classified intent: {Intent} ({Category}) with confidence {Confidence}",
            result.Intent, result.Category, result.Confidence);

        return Task.FromResult(result);
    }

    private void AdjustForContext(IntentResult result, IReadOnlyList<ChatMessage> context)
    {
        var lastAssistantMessage = context.LastOrDefault(m => m.Role == "assistant");

        // If last message was a question, this is likely a response
        if (lastAssistantMessage?.Content.EndsWith("?") == true)
        {
            if (result.Intent == IntentType.GeneralQuery)
            {
                result.Intent = IntentType.Clarification;
                result.Confidence = Math.Max(result.Confidence, 0.6);
            }
        }

        // If there's ongoing tool usage, adjust confidence
        if (context.Any(m => m.ToolCalls != null && m.ToolCalls.Any()))
        {
            if (result.Category == IntentCategory.Task)
            {
                result.Confidence = Math.Min(result.Confidence + 0.1, 1.0);
            }
        }
    }

    private static List<IntentPattern> InitializePatterns()
    {
        return new List<IntentPattern>
        {
            // Code operations
            new IntentPattern(IntentType.CodeWrite, IntentCategory.Task,
                new[] { "write", "create", "implement", "add", "build", "make" },
                new[] { "code", "function", "class", "method", "file", "component", "service" }),

            new IntentPattern(IntentType.CodeRead, IntentCategory.Information,
                new[] { "read", "show", "display", "get", "open", "view" },
                new[] { "file", "code", "content", "source" }),

            new IntentPattern(IntentType.CodeEdit, IntentCategory.Task,
                new[] { "edit", "modify", "change", "update", "fix", "refactor" },
                new[] { "code", "function", "class", "file", "bug", "issue" }),

            new IntentPattern(IntentType.CodeExplain, IntentCategory.Information,
                new[] { "explain", "describe", "what does", "how does", "why" },
                new[] { "code", "function", "work", "mean", "do" }),

            // File operations
            new IntentPattern(IntentType.FileList, IntentCategory.Information,
                new[] { "list", "show", "what", "find" },
                new[] { "files", "directory", "folder", "contents" }),

            new IntentPattern(IntentType.FileCreate, IntentCategory.Task,
                new[] { "create", "make", "new" },
                new[] { "file", "folder", "directory" }),

            new IntentPattern(IntentType.FileDelete, IntentCategory.Task,
                new[] { "delete", "remove", "rm" },
                new[] { "file", "folder", "directory" }),

            // Shell operations
            new IntentPattern(IntentType.ShellCommand, IntentCategory.Task,
                new[] { "run", "execute", "shell", "terminal", "command", "npm", "git", "dotnet" },
                Array.Empty<string>()),

            // Planning
            new IntentPattern(IntentType.Planning, IntentCategory.Planning,
                new[] { "plan", "how should", "what steps", "break down", "approach" },
                new[] { "implement", "build", "create", "solve" }),

            // Search/query
            new IntentPattern(IntentType.Search, IntentCategory.Information,
                new[] { "search", "find", "look for", "where is", "locate" },
                Array.Empty<string>()),

            // Help
            new IntentPattern(IntentType.Help, IntentCategory.Information,
                new[] { "help", "how to", "can you", "what can" },
                Array.Empty<string>()),

            // Confirmation/feedback
            new IntentPattern(IntentType.Confirmation, IntentCategory.Feedback,
                new[] { "yes", "ok", "sure", "go ahead", "proceed", "do it" },
                Array.Empty<string>()),

            new IntentPattern(IntentType.Rejection, IntentCategory.Feedback,
                new[] { "no", "don't", "stop", "cancel", "nevermind" },
                Array.Empty<string>()),
        };
    }
}

/// <summary>
/// Pattern for matching intents.
/// </summary>
internal class IntentPattern
{
    public IntentType Intent { get; }
    public IntentCategory Category { get; }
    private readonly string[] _triggerWords;
    private readonly string[] _contextWords;

    public IntentPattern(IntentType intent, IntentCategory category, string[] triggerWords, string[] contextWords)
    {
        Intent = intent;
        Category = category;
        _triggerWords = triggerWords;
        _contextWords = contextWords;
    }

    public PatternMatch Match(string text)
    {
        var hasTrigger = _triggerWords.Length == 0 || _triggerWords.Any(t => text.Contains(t));
        var hasContext = _contextWords.Length == 0 || _contextWords.Any(c => text.Contains(c));

        if (!hasTrigger)
        {
            return new PatternMatch { IsMatch = false };
        }

        var confidence = 0.5;

        // Boost confidence for trigger matches
        var triggerMatches = _triggerWords.Count(t => text.Contains(t));
        confidence += triggerMatches * 0.1;

        // Boost for context matches
        if (hasContext)
        {
            var contextMatches = _contextWords.Count(c => text.Contains(c));
            confidence += contextMatches * 0.1;
        }
        else if (_contextWords.Length > 0)
        {
            // Reduce confidence if context words expected but not found
            confidence -= 0.2;
        }

        return new PatternMatch
        {
            IsMatch = confidence > 0.3,
            Confidence = Math.Min(confidence, 1.0),
            Entities = ExtractEntities(text)
        };
    }

    private Dictionary<string, string> ExtractEntities(string text)
    {
        var entities = new Dictionary<string, string>();

        // Extract quoted strings
        var quotePattern = new System.Text.RegularExpressions.Regex("\"([^\"]+)\"");
        var matches = quotePattern.Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
            entities[$"quoted_{i}"] = matches[i].Groups[1].Value;
        }

        // Extract file paths
        var pathPattern = new System.Text.RegularExpressions.Regex(@"[\w./\\]+\.\w+");
        var pathMatches = pathPattern.Matches(text);
        for (var i = 0; i < pathMatches.Count; i++)
        {
            entities[$"path_{i}"] = pathMatches[i].Value;
        }

        return entities;
    }
}

/// <summary>
/// Result of pattern matching.
/// </summary>
internal class PatternMatch
{
    public bool IsMatch { get; set; }
    public double Confidence { get; set; }
    public Dictionary<string, string> Entities { get; set; } = new();
}
