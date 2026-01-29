using Microsoft.Extensions.Options;

namespace LittleHelperAI.Backend.Infrastructure;

/// <summary>
/// Configuration for request timeout settings.
/// </summary>
public class RequestTimeoutOptions
{
    public const string SectionName = "RequestTimeout";

    /// <summary>
    /// Maximum total request duration in minutes.
    /// </summary>
    public int MaxRequestDurationMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum inference duration in minutes.
    /// </summary>
    public int MaxInferenceDurationMinutes { get; set; } = 3;

    /// <summary>
    /// Maximum tool execution duration in seconds.
    /// </summary>
    public int MaxToolDurationSeconds { get; set; } = 30;

    public TimeSpan MaxRequestDuration => TimeSpan.FromMinutes(MaxRequestDurationMinutes);
    public TimeSpan MaxInferenceDuration => TimeSpan.FromMinutes(MaxInferenceDurationMinutes);
    public TimeSpan MaxToolDuration => TimeSpan.FromSeconds(MaxToolDurationSeconds);
}

/// <summary>
/// Manages request-scoped timeouts for pipeline operations.
/// </summary>
public interface IRequestTimeoutService
{
    /// <summary>
    /// Creates a request-scoped timeout context.
    /// </summary>
    RequestTimeoutContext CreateContext(CancellationToken externalToken = default);

    /// <summary>
    /// Gets current timeout options.
    /// </summary>
    RequestTimeoutOptions Options { get; }
}

/// <summary>
/// Request-scoped timeout context that manages cancellation tokens.
/// </summary>
public sealed class RequestTimeoutContext : IDisposable
{
    private readonly CancellationTokenSource _requestCts;
    private readonly CancellationTokenSource _linkedCts;
    private readonly DateTime _startTime;
    private bool _disposed;

    public RequestTimeoutContext(RequestTimeoutOptions options, CancellationToken externalToken)
    {
        Options = options;
        _startTime = DateTime.UtcNow;
        _requestCts = new CancellationTokenSource(options.MaxRequestDuration);
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _requestCts.Token,
            externalToken);
    }

    public RequestTimeoutOptions Options { get; }

    /// <summary>
    /// Token that cancels when the request timeout is exceeded.
    /// </summary>
    public CancellationToken RequestToken => _linkedCts.Token;

    /// <summary>
    /// Elapsed time since request started.
    /// </summary>
    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;

    /// <summary>
    /// Remaining time before timeout.
    /// </summary>
    public TimeSpan Remaining => Options.MaxRequestDuration - Elapsed;

    /// <summary>
    /// Whether the request has timed out.
    /// </summary>
    public bool IsTimedOut => _requestCts.IsCancellationRequested;

    /// <summary>
    /// Creates a timeout token for inference operations.
    /// </summary>
    public CancellationToken CreateInferenceToken()
    {
        var effectiveTimeout = TimeSpan.FromTicks(Math.Min(
            Options.MaxInferenceDuration.Ticks,
            Remaining.Ticks));

        if (effectiveTimeout <= TimeSpan.Zero)
            return new CancellationToken(true);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(RequestToken);
        cts.CancelAfter(effectiveTimeout);
        return cts.Token;
    }

    /// <summary>
    /// Creates a timeout token for tool operations.
    /// </summary>
    public CancellationToken CreateToolToken()
    {
        var effectiveTimeout = TimeSpan.FromTicks(Math.Min(
            Options.MaxToolDuration.Ticks,
            Remaining.Ticks));

        if (effectiveTimeout <= TimeSpan.Zero)
            return new CancellationToken(true);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(RequestToken);
        cts.CancelAfter(effectiveTimeout);
        return cts.Token;
    }

    /// <summary>
    /// Throws if the request has timed out.
    /// </summary>
    public void ThrowIfTimedOut()
    {
        if (IsTimedOut)
        {
            throw new RequestTimeoutException(
                $"Request exceeded maximum duration of {Options.MaxRequestDurationMinutes} minutes.",
                Elapsed);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _linkedCts.Dispose();
        _requestCts.Dispose();
    }
}

/// <summary>
/// Exception thrown when a request times out.
/// </summary>
public class RequestTimeoutException : OperationCanceledException
{
    public TimeSpan ElapsedTime { get; }

    public RequestTimeoutException(string message, TimeSpan elapsedTime)
        : base(message)
    {
        ElapsedTime = elapsedTime;
    }

    public RequestTimeoutException(string message, TimeSpan elapsedTime, Exception innerException)
        : base(message, innerException)
    {
        ElapsedTime = elapsedTime;
    }
}

/// <summary>
/// Implementation of request timeout service.
/// </summary>
public class RequestTimeoutService : IRequestTimeoutService
{
    private readonly RequestTimeoutOptions _options;

    public RequestTimeoutService(IOptions<RequestTimeoutOptions> options)
    {
        _options = options.Value;
    }

    public RequestTimeoutOptions Options => _options;

    public RequestTimeoutContext CreateContext(CancellationToken externalToken = default)
    {
        return new RequestTimeoutContext(_options, externalToken);
    }
}
