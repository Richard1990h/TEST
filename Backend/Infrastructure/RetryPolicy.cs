using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleHelperAI.Backend.Infrastructure;

/// <summary>
/// Configuration for retry policy.
/// </summary>
public class RetryPolicyOptions
{
    public const string SectionName = "RetryPolicy";

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay before first retry in milliseconds.
    /// </summary>
    public int InitialDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay between retries in milliseconds.
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;

    /// <summary>
    /// Multiplier for exponential backoff.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    public TimeSpan InitialDelay => TimeSpan.FromMilliseconds(InitialDelayMs);
    public TimeSpan MaxDelay => TimeSpan.FromMilliseconds(MaxDelayMs);
}

/// <summary>
/// Classification of LLM failures.
/// </summary>
public enum FailureType
{
    /// <summary>
    /// Transient failure that may succeed on retry.
    /// </summary>
    Transient,

    /// <summary>
    /// Permanent failure that should not be retried.
    /// </summary>
    Permanent,

    /// <summary>
    /// Rate limit exceeded, retry after delay.
    /// </summary>
    RateLimited,

    /// <summary>
    /// Service unavailable, may recover.
    /// </summary>
    ServiceUnavailable,

    /// <summary>
    /// Unknown failure type.
    /// </summary>
    Unknown
}

/// <summary>
/// Result of failure classification.
/// </summary>
public class FailureClassification
{
    public FailureType Type { get; init; }
    public bool ShouldRetry { get; init; }
    public TimeSpan? SuggestedDelay { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Classifies LLM failures to determine retry behavior.
/// </summary>
public interface ILlmFailureClassifier
{
    /// <summary>
    /// Classify an exception to determine if it should be retried.
    /// </summary>
    FailureClassification Classify(Exception exception);
}

/// <summary>
/// Implementation of failure classifier.
/// </summary>
public class LlmFailureClassifier : ILlmFailureClassifier
{
    private static readonly HashSet<string> TransientMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        "timeout",
        "connection reset",
        "connection refused",
        "temporarily unavailable",
        "service unavailable",
        "bad gateway",
        "gateway timeout",
        "network error",
        "socket exception"
    };

    private static readonly HashSet<string> PermanentMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        "invalid model",
        "model not found",
        "invalid request",
        "authentication failed",
        "unauthorized",
        "forbidden",
        "not found",
        "invalid api key"
    };

    public FailureClassification Classify(Exception exception)
    {
        // Check for cancellation
        if (exception is OperationCanceledException)
        {
            return new FailureClassification
            {
                Type = FailureType.Permanent,
                ShouldRetry = false,
                Reason = "Operation was cancelled"
            };
        }

        // Check for timeout specifically
        if (exception is TimeoutException || exception is RequestTimeoutException)
        {
            return new FailureClassification
            {
                Type = FailureType.Transient,
                ShouldRetry = true,
                Reason = "Request timed out"
            };
        }

        // Check for HTTP status codes
        if (exception is HttpRequestException httpEx)
        {
            return ClassifyHttpException(httpEx);
        }

        // Check message content
        var message = exception.Message.ToLowerInvariant();

        foreach (var transient in TransientMessages)
        {
            if (message.Contains(transient))
            {
                return new FailureClassification
                {
                    Type = FailureType.Transient,
                    ShouldRetry = true,
                    Reason = $"Transient error detected: {transient}"
                };
            }
        }

        foreach (var permanent in PermanentMessages)
        {
            if (message.Contains(permanent))
            {
                return new FailureClassification
                {
                    Type = FailureType.Permanent,
                    ShouldRetry = false,
                    Reason = $"Permanent error detected: {permanent}"
                };
            }
        }

        // Check inner exception
        if (exception.InnerException != null)
        {
            var innerClassification = Classify(exception.InnerException);
            if (innerClassification.Type != FailureType.Unknown)
            {
                return innerClassification;
            }
        }

        // Default to transient for unknown errors
        return new FailureClassification
        {
            Type = FailureType.Unknown,
            ShouldRetry = true,
            Reason = "Unknown error, will retry"
        };
    }

    private FailureClassification ClassifyHttpException(HttpRequestException httpEx)
    {
        var statusCode = httpEx.StatusCode;

        return statusCode switch
        {
            System.Net.HttpStatusCode.TooManyRequests => new FailureClassification
            {
                Type = FailureType.RateLimited,
                ShouldRetry = true,
                SuggestedDelay = TimeSpan.FromSeconds(30),
                Reason = "Rate limited"
            },
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout or
            System.Net.HttpStatusCode.BadGateway => new FailureClassification
            {
                Type = FailureType.ServiceUnavailable,
                ShouldRetry = true,
                Reason = "Service temporarily unavailable"
            },
            System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden => new FailureClassification
            {
                Type = FailureType.Permanent,
                ShouldRetry = false,
                Reason = "Authentication/authorization error"
            },
            System.Net.HttpStatusCode.BadRequest or
            System.Net.HttpStatusCode.NotFound => new FailureClassification
            {
                Type = FailureType.Permanent,
                ShouldRetry = false,
                Reason = "Invalid request"
            },
            System.Net.HttpStatusCode.InternalServerError => new FailureClassification
            {
                Type = FailureType.Transient,
                ShouldRetry = true,
                Reason = "Server error, may be transient"
            },
            _ => new FailureClassification
            {
                Type = FailureType.Unknown,
                ShouldRetry = true,
                Reason = $"HTTP error: {statusCode}"
            }
        };
    }
}

/// <summary>
/// Retry policy for LLM operations with exponential backoff.
/// </summary>
public interface ILlmRetryPolicy
{
    /// <summary>
    /// Execute an operation with retry logic.
    /// </summary>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute an async enumerable operation with retry logic.
    /// </summary>
    IAsyncEnumerable<T> ExecuteStreamAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of retry policy with exponential backoff.
/// </summary>
public class LlmRetryPolicy : ILlmRetryPolicy
{
    private readonly ILogger<LlmRetryPolicy> _logger;
    private readonly RetryPolicyOptions _options;
    private readonly ILlmFailureClassifier _classifier;

    public LlmRetryPolicy(
        ILogger<LlmRetryPolicy> logger,
        IOptions<RetryPolicyOptions> options,
        ILlmFailureClassifier classifier)
    {
        _logger = logger;
        _options = options.Value;
        _classifier = classifier;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var delay = _options.InitialDelay;

        while (true)
        {
            attempt++;
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var classification = _classifier.Classify(ex);

                if (!classification.ShouldRetry || attempt >= _options.MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "LLM operation failed after {Attempt} attempts. Classification: {Type}, Reason: {Reason}",
                        attempt,
                        classification.Type,
                        classification.Reason);
                    throw;
                }

                var actualDelay = classification.SuggestedDelay ?? delay;
                if (actualDelay > _options.MaxDelay)
                    actualDelay = _options.MaxDelay;

                _logger.LogWarning(
                    ex,
                    "LLM operation failed (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}ms. Classification: {Type}",
                    attempt,
                    _options.MaxRetries,
                    actualDelay.TotalMilliseconds,
                    classification.Type);

                await Task.Delay(actualDelay, cancellationToken);

                // Exponential backoff
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * _options.BackoffMultiplier, _options.MaxDelayMs));
            }
        }
    }

    public async IAsyncEnumerable<T> ExecuteStreamAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>> operation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var delay = _options.InitialDelay;
        var hasYieldedAny = false;

        while (true)
        {
            attempt++;
            hasYieldedAny = false;

            IAsyncEnumerator<T>? enumerator = null;
            Exception? caughtException = null;

            try
            {
                enumerator = operation(cancellationToken).GetAsyncEnumerator(cancellationToken);

                while (true)
                {
                    bool moveNext;
                    try
                    {
                        moveNext = await enumerator.MoveNextAsync();
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        caughtException = ex;
                        break;
                    }

                    if (!moveNext)
                    {
                        // Stream completed successfully
                        yield break;
                    }

                    hasYieldedAny = true;
                    yield return enumerator.Current;
                }
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync();
                }
            }

            if (caughtException != null)
            {
                // Don't retry if we've already yielded items (partial success)
                if (hasYieldedAny)
                {
                    _logger.LogWarning(
                        caughtException,
                        "LLM stream failed after yielding items. Not retrying partial stream.");
                    throw caughtException;
                }

                var classification = _classifier.Classify(caughtException);

                if (!classification.ShouldRetry || attempt >= _options.MaxRetries)
                {
                    _logger.LogError(
                        caughtException,
                        "LLM stream failed after {Attempt} attempts. Classification: {Type}",
                        attempt,
                        classification.Type);
                    throw caughtException;
                }

                var actualDelay = classification.SuggestedDelay ?? delay;
                if (actualDelay > _options.MaxDelay)
                    actualDelay = _options.MaxDelay;

                _logger.LogWarning(
                    caughtException,
                    "LLM stream failed (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}ms.",
                    attempt,
                    _options.MaxRetries,
                    actualDelay.TotalMilliseconds);

                await Task.Delay(actualDelay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * _options.BackoffMultiplier, _options.MaxDelayMs));
            }
        }
    }
}
