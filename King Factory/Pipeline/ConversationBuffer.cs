using LittleHelperAI.KingFactory.Models;
using System.Collections.Concurrent;

namespace LittleHelperAI.KingFactory.Pipeline;

/// <summary>
/// Manages conversation history with windowing support.
/// </summary>
public interface IConversationBuffer
{
    /// <summary>
    /// Add a message to the conversation.
    /// </summary>
    void AddMessage(ChatMessage message);

    /// <summary>
    /// Get all messages in the conversation.
    /// </summary>
    IReadOnlyList<ChatMessage> GetMessages();

    /// <summary>
    /// Get messages within a token window.
    /// </summary>
    IReadOnlyList<ChatMessage> GetWindowedMessages(int maxTokens);

    /// <summary>
    /// Clear the conversation history.
    /// </summary>
    void Clear();

    /// <summary>
    /// Get the conversation ID.
    /// </summary>
    string ConversationId { get; }

    /// <summary>
    /// Estimated total tokens in the conversation.
    /// </summary>
    int EstimatedTokenCount { get; }
}

/// <summary>
/// Thread-safe conversation buffer implementation.
/// </summary>
public class ConversationBuffer : IConversationBuffer
{
    private readonly List<ChatMessage> _messages = new();
    private readonly object _lock = new();
    private readonly int _maxMessages;

    public string ConversationId { get; }

    public ConversationBuffer(string? conversationId = null, int maxMessages = 100)
    {
        ConversationId = conversationId ?? Guid.NewGuid().ToString();
        _maxMessages = maxMessages;
    }

    public void AddMessage(ChatMessage message)
    {
        lock (_lock)
        {
            _messages.Add(message);

            // Trim oldest messages if we exceed max (keep system messages)
            while (_messages.Count > _maxMessages)
            {
                var firstNonSystem = _messages.FindIndex(m => m.Role != "system");
                if (firstNonSystem >= 0)
                {
                    _messages.RemoveAt(firstNonSystem);
                }
                else
                {
                    break;
                }
            }
        }
    }

    public IReadOnlyList<ChatMessage> GetMessages()
    {
        lock (_lock)
        {
            return _messages.ToList().AsReadOnly();
        }
    }

    public IReadOnlyList<ChatMessage> GetWindowedMessages(int maxTokens)
    {
        lock (_lock)
        {
            var result = new List<ChatMessage>();
            var tokenCount = 0;

            // Always include system messages first
            var systemMessages = _messages.Where(m => m.Role == "system").ToList();
            foreach (var msg in systemMessages)
            {
                result.Add(msg);
                tokenCount += EstimateTokens(msg.Content);
            }

            // Add recent messages from newest to oldest until we hit the limit
            var nonSystemMessages = _messages.Where(m => m.Role != "system").Reverse().ToList();
            var messagesToAdd = new List<ChatMessage>();

            foreach (var msg in nonSystemMessages)
            {
                var msgTokens = EstimateTokens(msg.Content);
                if (tokenCount + msgTokens > maxTokens)
                    break;

                messagesToAdd.Add(msg);
                tokenCount += msgTokens;
            }

            // Reverse to maintain chronological order
            messagesToAdd.Reverse();
            result.AddRange(messagesToAdd);

            return result.AsReadOnly();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
        }
    }

    public int EstimatedTokenCount
    {
        get
        {
            lock (_lock)
            {
                return _messages.Sum(m => EstimateTokens(m.Content));
            }
        }
    }

    /// <summary>
    /// Rough token estimation (approximately 4 chars per token).
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / 4.0);
    }
}

/// <summary>
/// Manages multiple conversation buffers.
/// </summary>
public interface IConversationManager
{
    IConversationBuffer GetOrCreate(string conversationId);
    void Remove(string conversationId);
    bool TryGet(string conversationId, out IConversationBuffer? buffer);
}

/// <summary>
/// Thread-safe conversation manager.
/// </summary>
public class ConversationManager : IConversationManager
{
    private readonly ConcurrentDictionary<string, IConversationBuffer> _conversations = new();
    private readonly int _maxMessages;

    public ConversationManager(int maxMessages = 100)
    {
        _maxMessages = maxMessages;
    }

    public IConversationBuffer GetOrCreate(string conversationId)
    {
        return _conversations.GetOrAdd(conversationId, id => new ConversationBuffer(id, _maxMessages));
    }

    public void Remove(string conversationId)
    {
        _conversations.TryRemove(conversationId, out _);
    }

    public bool TryGet(string conversationId, out IConversationBuffer? buffer)
    {
        var found = _conversations.TryGetValue(conversationId, out var buf);
        buffer = buf;
        return found;
    }
}
