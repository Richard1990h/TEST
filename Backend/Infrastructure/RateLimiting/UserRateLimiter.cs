using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace LittleHelperAI.Backend.Infrastructure.RateLimiting;

/// <summary>
/// Configuration for rate limiting.
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Default requests per minute for users without a plan.
    /// </summary>
    public int DefaultRequestsPerMinute { get; set; } = 5;

    /// <summary>
    /// Default concurrent request limit.
    /// </summary>
    public int DefaultConcurrentRequests { get; set; } = 1;

    /// <summary>
    /// Limits per plan type.
    /// </summary>
    public Dictionary<string, PlanLimits> PlanLimits { get; set; } = new()
    {
        ["FREE"] = new PlanLimits { RequestsPerMinute = 5, ConcurrentRequests = 1 },
        ["BASIC"] = new PlanLimits { RequestsPerMinute = 20, ConcurrentRequests = 2 },
        ["PRO"] = new PlanLimits { RequestsPerMinute = 60, ConcurrentRequests = 3 }
    };
}

/// <summary>
/// Rate limits for a specific plan.
/// </summary>
public class PlanLimits
{
    public int RequestsPerMinute { get; set; }
    public int ConcurrentRequests { get; set; }
}

/// <summary>
/// Result of a rate limit check.
/// </summary>
public class RateLimitResult
{
    /// <summary>
    /// Whether the request is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Current request count in the window.
    /// </summary>
    public int CurrentCount { get; init; }

    /// <summary>
    /// Maximum requests allowed in the window.
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// Requests remaining in the current window.
    /// </summary>
    public int Remaining { get; init; }

    /// <summary>
    /// Time until the rate limit resets.
    /// </summary>
    public TimeSpan RetryAfter { get; init; }

    /// <summary>
    /// Current concurrent requests.
    /// </summary>
    public int ConcurrentCount { get; init; }

    /// <summary>
    /// Maximum concurrent requests allowed.
    /// </summary>
    public int ConcurrentLimit { get; init; }

    /// <summary>
    /// Reason for rejection (if not allowed).
    /// </summary>
    public string? RejectionReason { get; init; }

    public static RateLimitResult Allowed(int currentCount, int limit, int concurrentCount, int concurrentLimit) => new()
    {
        IsAllowed = true,
        CurrentCount = currentCount,
        Limit = limit,
        Remaining = limit - currentCount,
        ConcurrentCount = concurrentCount,
        ConcurrentLimit = concurrentLimit
    };

    public static RateLimitResult RateLimited(int currentCount, int limit, TimeSpan retryAfter) => new()
    {
        IsAllowed = false,
        CurrentCount = currentCount,
        Limit = limit,
        Remaining = 0,
        RetryAfter = retryAfter,
        RejectionReason = "Rate limit exceeded"
    };

    public static RateLimitResult ConcurrencyLimited(int concurrentCount, int concurrentLimit) => new()
    {
        IsAllowed = false,
        ConcurrentCount = concurrentCount,
        ConcurrentLimit = concurrentLimit,
        Remaining = 0,
        RetryAfter = TimeSpan.FromSeconds(5),
        RejectionReason = "Too many concurrent requests"
    };
}

/// <summary>
/// Tracks rate limiting state for a user.
/// </summary>
internal class UserRateLimitState
{
    public ConcurrentQueue<DateTime> RequestTimestamps { get; } = new();
    public int ConcurrentRequests;
    public DateTime LastCleanup = DateTime.UtcNow;
}

/// <summary>
/// Rate limiter for per-user request throttling.
/// </summary>
public interface IUserRateLimiter
{
    /// <summary>
    /// Check if a request is allowed for a user.
    /// </summary>
    RateLimitResult CheckLimit(int userId, string? planType = null);

    /// <summary>
    /// Acquire a rate limit slot for a user.
    /// Returns a disposable that releases the concurrent slot on disposal.
    /// </summary>
    (RateLimitResult result, IDisposable? releaser) TryAcquire(int userId, string? planType = null);

    /// <summary>
    /// Get current stats for a user.
    /// </summary>
    RateLimitResult GetStats(int userId, string? planType = null);
}

/// <summary>
/// Implementation of user rate limiter using sliding window.
/// </summary>
public class UserRateLimiter : IUserRateLimiter
{
    private readonly ILogger<UserRateLimiter> _logger;
    private readonly RateLimitingOptions _options;
    private readonly ConcurrentDictionary<int, UserRateLimitState> _userStates = new();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    public UserRateLimiter(
        ILogger<UserRateLimiter> logger,
        IOptions<RateLimitingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public RateLimitResult CheckLimit(int userId, string? planType = null)
    {
        var (limits, state) = GetLimitsAndState(userId, planType);
        CleanupOldRequests(state);

        var currentCount = state.RequestTimestamps.Count;
        var concurrentCount = state.ConcurrentRequests;

        if (concurrentCount >= limits.ConcurrentRequests)
        {
            return RateLimitResult.ConcurrencyLimited(concurrentCount, limits.ConcurrentRequests);
        }

        if (currentCount >= limits.RequestsPerMinute)
        {
            var retryAfter = CalculateRetryAfter(state);
            return RateLimitResult.RateLimited(currentCount, limits.RequestsPerMinute, retryAfter);
        }

        return RateLimitResult.Allowed(currentCount, limits.RequestsPerMinute, concurrentCount, limits.ConcurrentRequests);
    }

    public (RateLimitResult result, IDisposable? releaser) TryAcquire(int userId, string? planType = null)
    {
        var (limits, state) = GetLimitsAndState(userId, planType);
        CleanupOldRequests(state);

        var currentCount = state.RequestTimestamps.Count;
        var concurrentCount = Interlocked.Increment(ref state.ConcurrentRequests);

        // Check concurrent limit
        if (concurrentCount > limits.ConcurrentRequests)
        {
            Interlocked.Decrement(ref state.ConcurrentRequests);
            _logger.LogWarning(
                "User {UserId} exceeded concurrent request limit ({Limit})",
                userId,
                limits.ConcurrentRequests);
            return (RateLimitResult.ConcurrencyLimited(concurrentCount - 1, limits.ConcurrentRequests), null);
        }

        // Check rate limit
        if (currentCount >= limits.RequestsPerMinute)
        {
            Interlocked.Decrement(ref state.ConcurrentRequests);
            var retryAfter = CalculateRetryAfter(state);
            _logger.LogWarning(
                "User {UserId} exceeded rate limit ({Count}/{Limit}). Retry after {RetryAfter}s",
                userId,
                currentCount,
                limits.RequestsPerMinute,
                retryAfter.TotalSeconds);
            return (RateLimitResult.RateLimited(currentCount, limits.RequestsPerMinute, retryAfter), null);
        }

        // Record the request
        state.RequestTimestamps.Enqueue(DateTime.UtcNow);

        var releaser = new RateLimitReleaser(state);
        var result = RateLimitResult.Allowed(
            currentCount + 1,
            limits.RequestsPerMinute,
            concurrentCount,
            limits.ConcurrentRequests);

        _logger.LogDebug(
            "User {UserId} acquired rate limit slot ({Count}/{Limit}, {Concurrent}/{ConcurrentLimit})",
            userId,
            result.CurrentCount,
            result.Limit,
            result.ConcurrentCount,
            result.ConcurrentLimit);

        return (result, releaser);
    }

    public RateLimitResult GetStats(int userId, string? planType = null)
    {
        var (limits, state) = GetLimitsAndState(userId, planType);
        CleanupOldRequests(state);

        var currentCount = state.RequestTimestamps.Count;
        var concurrentCount = state.ConcurrentRequests;

        return new RateLimitResult
        {
            IsAllowed = currentCount < limits.RequestsPerMinute && concurrentCount < limits.ConcurrentRequests,
            CurrentCount = currentCount,
            Limit = limits.RequestsPerMinute,
            Remaining = Math.Max(0, limits.RequestsPerMinute - currentCount),
            ConcurrentCount = concurrentCount,
            ConcurrentLimit = limits.ConcurrentRequests
        };
    }

    private (PlanLimits limits, UserRateLimitState state) GetLimitsAndState(int userId, string? planType)
    {
        var limits = GetLimitsForPlan(planType);
        var state = _userStates.GetOrAdd(userId, _ => new UserRateLimitState());
        return (limits, state);
    }

    private PlanLimits GetLimitsForPlan(string? planType)
    {
        if (string.IsNullOrEmpty(planType))
        {
            return new PlanLimits
            {
                RequestsPerMinute = _options.DefaultRequestsPerMinute,
                ConcurrentRequests = _options.DefaultConcurrentRequests
            };
        }

        return _options.PlanLimits.TryGetValue(planType.ToUpperInvariant(), out var limits)
            ? limits
            : new PlanLimits
            {
                RequestsPerMinute = _options.DefaultRequestsPerMinute,
                ConcurrentRequests = _options.DefaultConcurrentRequests
            };
    }

    private static void CleanupOldRequests(UserRateLimitState state)
    {
        var cutoff = DateTime.UtcNow - Window;
        while (state.RequestTimestamps.TryPeek(out var oldest) && oldest < cutoff)
        {
            state.RequestTimestamps.TryDequeue(out _);
        }

        // Periodic cleanup of stale entries
        if (DateTime.UtcNow - state.LastCleanup > CleanupInterval)
        {
            state.LastCleanup = DateTime.UtcNow;
        }
    }

    private static TimeSpan CalculateRetryAfter(UserRateLimitState state)
    {
        if (state.RequestTimestamps.TryPeek(out var oldest))
        {
            var elapsed = DateTime.UtcNow - oldest;
            var remaining = Window - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        return TimeSpan.Zero;
    }

    private sealed class RateLimitReleaser : IDisposable
    {
        private readonly UserRateLimitState _state;
        private int _disposed;

        public RateLimitReleaser(UserRateLimitState state)
        {
            _state = state;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _state.ConcurrentRequests);
            }
        }
    }
}

/// <summary>
/// Exception thrown when rate limit is exceeded.
/// </summary>
public class RateLimitExceededException : Exception
{
    public int UserId { get; }
    public TimeSpan RetryAfter { get; }
    public int CurrentCount { get; }
    public int Limit { get; }

    public RateLimitExceededException(int userId, RateLimitResult result)
        : base($"Rate limit exceeded for user {userId}. {result.RejectionReason}")
    {
        UserId = userId;
        RetryAfter = result.RetryAfter;
        CurrentCount = result.CurrentCount;
        Limit = result.Limit;
    }
}
