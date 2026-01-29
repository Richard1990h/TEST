using System.Threading.Channels;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// Thread-safe queue for non-blocking chat history persistence.
/// Avoids holding DbContext on the request thread.
/// </summary>
public sealed class ChatHistoryWriteQueue
{
    private readonly Channel<ChatHistory> _channel = Channel.CreateUnbounded<ChatHistory>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public bool TryEnqueue(ChatHistory item) => _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<ChatHistory> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
