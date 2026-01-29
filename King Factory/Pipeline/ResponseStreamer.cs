using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Pipeline;

/// <summary>
/// Handles streaming responses to clients.
/// </summary>
public interface IResponseStreamer
{
    /// <summary>
    /// Stream tokens to a callback.
    /// </summary>
    Task StreamAsync(IAsyncEnumerable<string> tokens, Func<string, Task> onToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream tokens with buffering for smoother output.
    /// </summary>
    Task StreamBufferedAsync(IAsyncEnumerable<string> tokens, Func<string, Task> onToken, int bufferSize = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream with progress reporting.
    /// </summary>
    Task<string> StreamWithProgressAsync(IAsyncEnumerable<string> tokens, Func<string, int, Task> onProgress, CancellationToken cancellationToken = default);
}

/// <summary>
/// Response streaming implementation.
/// </summary>
public class ResponseStreamer : IResponseStreamer
{
    private readonly ILogger<ResponseStreamer> _logger;

    public ResponseStreamer(ILogger<ResponseStreamer> logger)
    {
        _logger = logger;
    }

    public async Task StreamAsync(IAsyncEnumerable<string> tokens, Func<string, Task> onToken, CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var token in tokens.WithCancellation(cancellationToken))
            {
                await onToken(token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during streaming");
            throw;
        }
    }

    public async Task StreamBufferedAsync(IAsyncEnumerable<string> tokens, Func<string, Task> onToken, int bufferSize = 5, CancellationToken cancellationToken = default)
    {
        var buffer = new List<string>();

        try
        {
            await foreach (var token in tokens.WithCancellation(cancellationToken))
            {
                buffer.Add(token);

                if (buffer.Count >= bufferSize)
                {
                    await onToken(string.Concat(buffer));
                    buffer.Clear();
                }
            }

            // Flush remaining buffer
            if (buffer.Count > 0)
            {
                await onToken(string.Concat(buffer));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Buffered streaming cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during buffered streaming");
            throw;
        }
    }

    public async Task<string> StreamWithProgressAsync(IAsyncEnumerable<string> tokens, Func<string, int, Task> onProgress, CancellationToken cancellationToken = default)
    {
        var fullResponse = new System.Text.StringBuilder();
        var tokenCount = 0;

        try
        {
            await foreach (var token in tokens.WithCancellation(cancellationToken))
            {
                fullResponse.Append(token);
                tokenCount++;
                await onProgress(token, tokenCount);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Progress streaming cancelled at {TokenCount} tokens", tokenCount);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during progress streaming at {TokenCount} tokens", tokenCount);
            throw;
        }

        return fullResponse.ToString();
    }
}

/// <summary>
/// Stream processing utilities.
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    /// Detect stop sequences in a stream and stop early.
    /// </summary>
    public static async IAsyncEnumerable<string> WithStopSequences(
        this IAsyncEnumerable<string> source,
        IEnumerable<string> stopSequences,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new System.Text.StringBuilder();
        var stops = stopSequences.ToList();
        var maxStopLength = stops.Max(s => s.Length);

        await foreach (var token in source.WithCancellation(cancellationToken))
        {
            buffer.Append(token);
            var text = buffer.ToString();

            // Check for complete stop sequences
            foreach (var stop in stops)
            {
                var index = text.IndexOf(stop, StringComparison.Ordinal);
                if (index >= 0)
                {
                    // Yield everything before the stop sequence
                    if (index > 0)
                    {
                        yield return text[..index];
                    }
                    yield break;
                }
            }

            // Yield tokens that can't possibly be part of a stop sequence
            if (buffer.Length > maxStopLength)
            {
                var safeLength = buffer.Length - maxStopLength;
                yield return buffer.ToString(0, safeLength);
                buffer.Remove(0, safeLength);
            }
        }

        // Yield remaining buffer
        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    /// <summary>
    /// Throttle stream output to a maximum rate.
    /// </summary>
    public static async IAsyncEnumerable<string> Throttled(
        this IAsyncEnumerable<string> source,
        TimeSpan minInterval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastYield = DateTime.MinValue;

        await foreach (var token in source.WithCancellation(cancellationToken))
        {
            var elapsed = DateTime.UtcNow - lastYield;
            if (elapsed < minInterval)
            {
                await Task.Delay(minInterval - elapsed, cancellationToken);
            }

            yield return token;
            lastYield = DateTime.UtcNow;
        }
    }
}
