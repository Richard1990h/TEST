using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace LittleHelperAI.Backend.Infrastructure;

/// <summary>
/// Circuit breaker state.
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed, requests flow through normally.
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open, all requests are rejected.
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is testing, limited requests allowed through.
    /// </summary>
    HalfOpen
}

/// <summary>
/// Configuration for the circuit breaker.
/// </summary>
public class CircuitBreakerOptions
{
    public const string SectionName = "CircuitBreaker";

    /// <summary>
    /// Number of failures before opening the circuit.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Time window for counting failures in seconds.
    /// </summary>
    public int FailureWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Duration the circuit stays open in seconds.
    /// </summary>
    public int OpenDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Number of successes needed to close the circuit from half-open.
    /// </summary>
    public int HalfOpenSuccessThreshold { get; set; } = 2;

    public TimeSpan FailureWindow => TimeSpan.FromSeconds(FailureWindowSeconds);
    public TimeSpan OpenDuration => TimeSpan.FromSeconds(OpenDurationSeconds);
}

/// <summary>
/// Circuit breaker event arguments.
/// </summary>
public class CircuitBreakerStateChangedEventArgs : EventArgs
{
    public CircuitState OldState { get; init; }
    public CircuitState NewState { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Exception thrown when the circuit breaker is open.
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public TimeSpan RetryAfter { get; }
    public DateTime OpenedAt { get; }

    public CircuitBreakerOpenException(TimeSpan retryAfter, DateTime openedAt)
        : base($"Circuit breaker is open. Retry after {retryAfter.TotalSeconds:F0} seconds.")
    {
        RetryAfter = retryAfter;
        OpenedAt = openedAt;
    }
}

/// <summary>
/// Circuit breaker for LLM operations.
/// </summary>
public interface ILlmCircuitBreaker
{
    /// <summary>
    /// Current state of the circuit.
    /// </summary>
    CircuitState State { get; }

    /// <summary>
    /// Whether requests are allowed through.
    /// </summary>
    bool AllowRequest();

    /// <summary>
    /// Record a successful request.
    /// </summary>
    void RecordSuccess();

    /// <summary>
    /// Record a failed request.
    /// </summary>
    void RecordFailure(Exception? exception = null);

    /// <summary>
    /// Get time until the circuit closes (if open).
    /// </summary>
    TimeSpan? GetRetryAfter();

    /// <summary>
    /// Get health statistics.
    /// </summary>
    CircuitBreakerStats GetStats();

    /// <summary>
    /// Event fired when state changes.
    /// </summary>
    event EventHandler<CircuitBreakerStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Circuit breaker statistics.
/// </summary>
public class CircuitBreakerStats
{
    public CircuitState State { get; init; }
    public int TotalRequests { get; init; }
    public int SuccessfulRequests { get; init; }
    public int FailedRequests { get; init; }
    public int RecentFailures { get; init; }
    public DateTime? LastFailure { get; init; }
    public DateTime? LastSuccess { get; init; }
    public DateTime? OpenedAt { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 1.0;
}

/// <summary>
/// Implementation of circuit breaker for LLM operations.
/// </summary>
public class LlmCircuitBreaker : ILlmCircuitBreaker
{
    private readonly ILogger<LlmCircuitBreaker> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly ConcurrentQueue<DateTime> _recentFailures = new();
    private readonly object _stateLock = new();

    private CircuitState _state = CircuitState.Closed;
    private DateTime? _openedAt;
    private int _halfOpenSuccesses;
    private int _totalRequests;
    private int _successfulRequests;
    private int _failedRequests;
    private DateTime? _lastFailure;
    private DateTime? _lastSuccess;

    public event EventHandler<CircuitBreakerStateChangedEventArgs>? StateChanged;

    public CircuitState State
    {
        get
        {
            lock (_stateLock)
            {
                EvaluateState();
                return _state;
            }
        }
    }

    public LlmCircuitBreaker(
        ILogger<LlmCircuitBreaker> logger,
        IOptions<CircuitBreakerOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public bool AllowRequest()
    {
        lock (_stateLock)
        {
            _totalRequests++;
            EvaluateState();

            switch (_state)
            {
                case CircuitState.Closed:
                    return true;

                case CircuitState.Open:
                    return false;

                case CircuitState.HalfOpen:
                    // Allow limited requests through for testing
                    return true;

                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_stateLock)
        {
            _successfulRequests++;
            _lastSuccess = DateTime.UtcNow;

            if (_state == CircuitState.HalfOpen)
            {
                _halfOpenSuccesses++;
                _logger.LogInformation(
                    "Circuit breaker half-open success {Count}/{Threshold}",
                    _halfOpenSuccesses,
                    _options.HalfOpenSuccessThreshold);

                if (_halfOpenSuccesses >= _options.HalfOpenSuccessThreshold)
                {
                    TransitionTo(CircuitState.Closed, "Sufficient successes in half-open state");
                }
            }
        }
    }

    public void RecordFailure(Exception? exception = null)
    {
        lock (_stateLock)
        {
            _failedRequests++;
            _lastFailure = DateTime.UtcNow;
            _recentFailures.Enqueue(DateTime.UtcNow);

            // Clean up old failures outside the window
            CleanupOldFailures();

            _logger.LogWarning(
                exception,
                "Circuit breaker recorded failure. Recent failures: {Count}/{Threshold}",
                GetRecentFailureCount(),
                _options.FailureThreshold);

            if (_state == CircuitState.HalfOpen)
            {
                // Any failure in half-open state opens the circuit
                TransitionTo(CircuitState.Open, "Failure during half-open test");
            }
            else if (_state == CircuitState.Closed && GetRecentFailureCount() >= _options.FailureThreshold)
            {
                TransitionTo(CircuitState.Open, $"Exceeded failure threshold ({_options.FailureThreshold} failures in {_options.FailureWindowSeconds}s)");
            }
        }
    }

    public TimeSpan? GetRetryAfter()
    {
        lock (_stateLock)
        {
            if (_state != CircuitState.Open || _openedAt == null)
                return null;

            var elapsed = DateTime.UtcNow - _openedAt.Value;
            var remaining = _options.OpenDuration - elapsed;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public CircuitBreakerStats GetStats()
    {
        lock (_stateLock)
        {
            CleanupOldFailures();
            EvaluateState();

            return new CircuitBreakerStats
            {
                State = _state,
                TotalRequests = _totalRequests,
                SuccessfulRequests = _successfulRequests,
                FailedRequests = _failedRequests,
                RecentFailures = GetRecentFailureCount(),
                LastFailure = _lastFailure,
                LastSuccess = _lastSuccess,
                OpenedAt = _openedAt,
                RetryAfter = GetRetryAfter()
            };
        }
    }

    private void EvaluateState()
    {
        if (_state == CircuitState.Open && _openedAt != null)
        {
            var elapsed = DateTime.UtcNow - _openedAt.Value;
            if (elapsed >= _options.OpenDuration)
            {
                TransitionTo(CircuitState.HalfOpen, "Open duration elapsed");
            }
        }
    }

    private void TransitionTo(CircuitState newState, string reason)
    {
        var oldState = _state;
        _state = newState;

        switch (newState)
        {
            case CircuitState.Open:
                _openedAt = DateTime.UtcNow;
                _halfOpenSuccesses = 0;
                break;

            case CircuitState.HalfOpen:
                _halfOpenSuccesses = 0;
                break;

            case CircuitState.Closed:
                _openedAt = null;
                _halfOpenSuccesses = 0;
                // Clear recent failures when closing
                while (_recentFailures.TryDequeue(out _)) { }
                break;
        }

        _logger.LogInformation(
            "Circuit breaker state changed: {OldState} -> {NewState}. Reason: {Reason}",
            oldState, newState, reason);

        StateChanged?.Invoke(this, new CircuitBreakerStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState,
            Reason = reason
        });
    }

    private void CleanupOldFailures()
    {
        var cutoff = DateTime.UtcNow - _options.FailureWindow;
        while (_recentFailures.TryPeek(out var oldest) && oldest < cutoff)
        {
            _recentFailures.TryDequeue(out _);
        }
    }

    private int GetRecentFailureCount()
    {
        return _recentFailures.Count;
    }
}
