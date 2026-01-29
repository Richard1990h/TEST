using LittleHelperAI.Backend.Infrastructure;
using LittleHelperAI.Backend.Infrastructure.RateLimiting;
using System.Collections.Concurrent;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// Pipeline execution metrics.
/// </summary>
public class PipelineExecutionMetrics
{
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public int TimeoutRequests { get; set; }
    public int RateLimitedRequests { get; set; }
    public int CircuitBreakerRejections { get; set; }
    public double AverageDurationMs { get; set; }
    public double AverageTtftMs { get; set; }
    public double AverageTokensPerSecond { get; set; }
    public int TotalTokensGenerated { get; set; }
    public int TotalToolCalls { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 1.0;
}

/// <summary>
/// Per-pipeline metrics breakdown.
/// </summary>
public class PipelineBreakdown
{
    public string PipelineId { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public double AverageDurationMs { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>
/// Complete metrics snapshot.
/// </summary>
public class MetricsSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan Uptime { get; set; }
    public PipelineExecutionMetrics Pipeline { get; set; } = new();
    public CircuitBreakerStats? CircuitBreaker { get; set; }
    public Dictionary<string, PipelineBreakdown> PipelineBreakdowns { get; set; } = new();
    public int ActiveRequests { get; set; }
    public int DeadLetterPending { get; set; }
    public long MemoryUsageMb { get; set; }
}

/// <summary>
/// Individual request record for metrics calculation.
/// </summary>
internal class RequestRecord
{
    public DateTime Timestamp { get; init; }
    public string PipelineId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public long DurationMs { get; init; }
    public long? TtftMs { get; init; }
    public double? TokensPerSecond { get; init; }
    public int? TokenCount { get; init; }
    public int ToolCalls { get; init; }
    public bool WasTimeout { get; init; }
    public bool WasRateLimited { get; init; }
    public bool WasCircuitBroken { get; init; }
}

/// <summary>
/// Service for collecting and aggregating pipeline metrics.
/// </summary>
public interface IPipelineMetrics
{
    /// <summary>
    /// Record a completed request.
    /// </summary>
    void RecordRequest(
        string pipelineId,
        bool success,
        long durationMs,
        long? ttftMs = null,
        double? tokensPerSecond = null,
        int? tokenCount = null,
        int toolCalls = 0);

    /// <summary>
    /// Record a timeout.
    /// </summary>
    void RecordTimeout(string pipelineId, long durationMs);

    /// <summary>
    /// Record a rate limit rejection.
    /// </summary>
    void RecordRateLimited(int userId);

    /// <summary>
    /// Record a circuit breaker rejection.
    /// </summary>
    void RecordCircuitBreakerRejection();

    /// <summary>
    /// Increment active request count.
    /// </summary>
    void IncrementActiveRequests();

    /// <summary>
    /// Decrement active request count.
    /// </summary>
    void DecrementActiveRequests();

    /// <summary>
    /// Get current metrics snapshot.
    /// </summary>
    MetricsSnapshot GetSnapshot();

    /// <summary>
    /// Get metrics for a specific time window.
    /// </summary>
    MetricsSnapshot GetSnapshot(TimeSpan window);

    /// <summary>
    /// Reset all metrics.
    /// </summary>
    void Reset();
}

/// <summary>
/// Implementation of pipeline metrics collection.
/// </summary>
public class PipelineMetrics : IPipelineMetrics
{
    private readonly ILlmCircuitBreaker? _circuitBreaker;
    private readonly IDeadLetterQueueService? _deadLetterQueue;
    private readonly DateTime _startTime = DateTime.UtcNow;

    private readonly ConcurrentQueue<RequestRecord> _records = new();
    private int _activeRequests;
    private int _rateLimitedCount;
    private int _circuitBreakerRejections;

    private const int MaxRecords = 10000;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(1);

    public PipelineMetrics(
        ILlmCircuitBreaker? circuitBreaker = null,
        IDeadLetterQueueService? deadLetterQueue = null)
    {
        _circuitBreaker = circuitBreaker;
        _deadLetterQueue = deadLetterQueue;
    }

    public void RecordRequest(
        string pipelineId,
        bool success,
        long durationMs,
        long? ttftMs = null,
        double? tokensPerSecond = null,
        int? tokenCount = null,
        int toolCalls = 0)
    {
        var record = new RequestRecord
        {
            Timestamp = DateTime.UtcNow,
            PipelineId = pipelineId,
            Success = success,
            DurationMs = durationMs,
            TtftMs = ttftMs,
            TokensPerSecond = tokensPerSecond,
            TokenCount = tokenCount,
            ToolCalls = toolCalls
        };

        _records.Enqueue(record);
        TrimRecords();
    }

    public void RecordTimeout(string pipelineId, long durationMs)
    {
        var record = new RequestRecord
        {
            Timestamp = DateTime.UtcNow,
            PipelineId = pipelineId,
            Success = false,
            DurationMs = durationMs,
            WasTimeout = true
        };

        _records.Enqueue(record);
        TrimRecords();
    }

    public void RecordRateLimited(int userId)
    {
        Interlocked.Increment(ref _rateLimitedCount);

        var record = new RequestRecord
        {
            Timestamp = DateTime.UtcNow,
            Success = false,
            WasRateLimited = true
        };

        _records.Enqueue(record);
        TrimRecords();
    }

    public void RecordCircuitBreakerRejection()
    {
        Interlocked.Increment(ref _circuitBreakerRejections);

        var record = new RequestRecord
        {
            Timestamp = DateTime.UtcNow,
            Success = false,
            WasCircuitBroken = true
        };

        _records.Enqueue(record);
        TrimRecords();
    }

    public void IncrementActiveRequests()
    {
        Interlocked.Increment(ref _activeRequests);
    }

    public void DecrementActiveRequests()
    {
        Interlocked.Decrement(ref _activeRequests);
    }

    public MetricsSnapshot GetSnapshot()
    {
        return GetSnapshot(DefaultWindow);
    }

    public MetricsSnapshot GetSnapshot(TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        var records = _records.Where(r => r.Timestamp >= cutoff).ToList();

        var pipelineMetrics = CalculatePipelineMetrics(records);
        var breakdowns = CalculateBreakdowns(records);

        int deadLetterPending = 0;
        if (_deadLetterQueue != null)
        {
            try
            {
                var summary = _deadLetterQueue.GetSummaryAsync().GetAwaiter().GetResult();
                deadLetterPending = summary.PendingCount;
            }
            catch
            {
                // Ignore errors getting DLQ stats
            }
        }

        return new MetricsSnapshot
        {
            Timestamp = DateTime.UtcNow,
            Uptime = DateTime.UtcNow - _startTime,
            Pipeline = pipelineMetrics,
            CircuitBreaker = _circuitBreaker?.GetStats(),
            PipelineBreakdowns = breakdowns,
            ActiveRequests = _activeRequests,
            DeadLetterPending = deadLetterPending,
            MemoryUsageMb = GC.GetTotalMemory(false) / (1024 * 1024)
        };
    }

    public void Reset()
    {
        while (_records.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _rateLimitedCount, 0);
        Interlocked.Exchange(ref _circuitBreakerRejections, 0);
    }

    private PipelineExecutionMetrics CalculatePipelineMetrics(List<RequestRecord> records)
    {
        var completedRequests = records.Where(r => !r.WasRateLimited && !r.WasCircuitBroken).ToList();

        return new PipelineExecutionMetrics
        {
            TotalRequests = records.Count,
            SuccessfulRequests = completedRequests.Count(r => r.Success),
            FailedRequests = completedRequests.Count(r => !r.Success && !r.WasTimeout),
            TimeoutRequests = records.Count(r => r.WasTimeout),
            RateLimitedRequests = records.Count(r => r.WasRateLimited),
            CircuitBreakerRejections = records.Count(r => r.WasCircuitBroken),
            AverageDurationMs = completedRequests.Any()
                ? completedRequests.Average(r => r.DurationMs)
                : 0,
            AverageTtftMs = completedRequests.Where(r => r.TtftMs.HasValue).Any()
                ? completedRequests.Where(r => r.TtftMs.HasValue).Average(r => r.TtftMs!.Value)
                : 0,
            AverageTokensPerSecond = completedRequests.Where(r => r.TokensPerSecond.HasValue).Any()
                ? completedRequests.Where(r => r.TokensPerSecond.HasValue).Average(r => r.TokensPerSecond!.Value)
                : 0,
            TotalTokensGenerated = completedRequests.Where(r => r.TokenCount.HasValue).Sum(r => r.TokenCount!.Value),
            TotalToolCalls = completedRequests.Sum(r => r.ToolCalls)
        };
    }

    private Dictionary<string, PipelineBreakdown> CalculateBreakdowns(List<RequestRecord> records)
    {
        return records
            .Where(r => !string.IsNullOrEmpty(r.PipelineId))
            .GroupBy(r => r.PipelineId)
            .ToDictionary(
                g => g.Key,
                g => new PipelineBreakdown
                {
                    PipelineId = g.Key,
                    RequestCount = g.Count(),
                    AverageDurationMs = g.Average(r => r.DurationMs),
                    SuccessCount = g.Count(r => r.Success),
                    FailureCount = g.Count(r => !r.Success)
                });
    }

    private void TrimRecords()
    {
        while (_records.Count > MaxRecords)
        {
            _records.TryDequeue(out _);
        }
    }
}
