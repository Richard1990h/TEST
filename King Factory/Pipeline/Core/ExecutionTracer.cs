using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// Traces pipeline execution for debugging and monitoring.
/// </summary>
public interface IExecutionTracer
{
    /// <summary>
    /// Begin tracing a new pipeline execution.
    /// </summary>
    ExecutionTrace BeginExecution(string pipelineId, string pipelineName, string conversationId, int? userId = null);

    /// <summary>
    /// Begin tracing a step within an execution.
    /// </summary>
    StepTrace BeginStep(string executionId, string stepId, string stepType);

    /// <summary>
    /// Get the trace for an execution.
    /// </summary>
    ExecutionTrace? GetTrace(string executionId);

    /// <summary>
    /// Get recent execution traces.
    /// </summary>
    IReadOnlyList<ExecutionTrace> GetRecentTraces(int count = 100);

    /// <summary>
    /// Clear old traces.
    /// </summary>
    void ClearOldTraces(TimeSpan olderThan);
}

/// <summary>
/// Default execution tracer implementation.
/// </summary>
public sealed class ExecutionTracer : IExecutionTracer
{
    private readonly ILogger<ExecutionTracer> _logger;
    private readonly ConcurrentDictionary<string, ExecutionTrace> _traces = new();
    private readonly ConcurrentQueue<string> _traceOrder = new();
    private readonly int _maxTraces;

    public ExecutionTracer(ILogger<ExecutionTracer> logger, int maxTraces = 1000)
    {
        _logger = logger;
        _maxTraces = maxTraces;
    }

    public ExecutionTrace BeginExecution(string pipelineId, string pipelineName, string conversationId, int? userId = null)
    {
        var trace = new ExecutionTrace
        {
            ExecutionId = Guid.NewGuid().ToString(),
            PipelineId = pipelineId,
            PipelineName = pipelineName,
            ConversationId = conversationId,
            UserId = userId,
            StartedAt = DateTime.UtcNow
        };

        _traces[trace.ExecutionId] = trace;
        _traceOrder.Enqueue(trace.ExecutionId);

        // Cleanup old traces if needed
        while (_traces.Count > _maxTraces && _traceOrder.TryDequeue(out var oldId))
        {
            _traces.TryRemove(oldId, out _);
        }

        _logger.LogDebug("Execution {ExecutionId} started for pipeline {PipelineId}", trace.ExecutionId, pipelineId);
        return trace;
    }

    public StepTrace BeginStep(string executionId, string stepId, string stepType)
    {
        if (!_traces.TryGetValue(executionId, out var trace))
        {
            throw new InvalidOperationException($"Execution {executionId} not found");
        }

        var stepTrace = new StepTrace
        {
            StepId = stepId,
            StepType = stepType,
            StartedAt = DateTime.UtcNow
        };

        trace.AddStep(stepTrace);

        _logger.LogDebug("Step {StepId} ({StepType}) started in execution {ExecutionId}", stepId, stepType, executionId);
        return stepTrace;
    }

    public ExecutionTrace? GetTrace(string executionId)
    {
        _traces.TryGetValue(executionId, out var trace);
        return trace;
    }

    public IReadOnlyList<ExecutionTrace> GetRecentTraces(int count = 100)
    {
        return _traces.Values
            .OrderByDescending(t => t.StartedAt)
            .Take(count)
            .ToList();
    }

    public void ClearOldTraces(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var toRemove = _traces.Values
            .Where(t => t.StartedAt < cutoff)
            .Select(t => t.ExecutionId)
            .ToList();

        foreach (var id in toRemove)
        {
            _traces.TryRemove(id, out _);
        }

        _logger.LogDebug("Cleared {Count} old traces", toRemove.Count);
    }
}

/// <summary>
/// Trace of a complete pipeline execution.
/// </summary>
public sealed class ExecutionTrace
{
    private readonly object _lock = new();
    private readonly List<StepTrace> _steps = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private ExecutionStatus _status = ExecutionStatus.Running;

    public string ExecutionId { get; init; } = string.Empty;
    public string PipelineId { get; init; } = string.Empty;
    public string PipelineName { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public int? UserId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; private set; }
    public ExecutionStatus Status => _status;
    public string? ErrorMessage { get; private set; }
    public long DurationMs => _stopwatch.ElapsedMilliseconds;
    public IReadOnlyList<StepTrace> Steps => _steps.ToList();
    public int CompletedStepCount => _steps.Count(s => s.Status == ExecutionStatus.Completed);
    public int FailedStepCount => _steps.Count(s => s.Status == ExecutionStatus.Failed);

    internal void AddStep(StepTrace step)
    {
        lock (_lock)
        {
            _steps.Add(step);
        }
    }

    public void Complete(bool success, string? errorMessage = null)
    {
        _stopwatch.Stop();
        CompletedAt = DateTime.UtcNow;
        _status = success ? ExecutionStatus.Completed : ExecutionStatus.Failed;
        ErrorMessage = errorMessage;
    }

    public ExecutionTraceSummary ToSummary()
    {
        return new ExecutionTraceSummary
        {
            ExecutionId = ExecutionId,
            PipelineId = PipelineId,
            PipelineName = PipelineName,
            ConversationId = ConversationId,
            UserId = UserId,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            Status = Status,
            DurationMs = DurationMs,
            StepCount = _steps.Count,
            CompletedStepCount = CompletedStepCount,
            FailedStepCount = FailedStepCount,
            ErrorMessage = ErrorMessage
        };
    }
}

/// <summary>
/// Trace of a single step execution.
/// </summary>
public sealed class StepTrace
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private ExecutionStatus _status = ExecutionStatus.Running;

    public string StepId { get; init; } = string.Empty;
    public string StepType { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; private set; }
    public ExecutionStatus Status => _status;
    public string? ErrorMessage { get; private set; }
    public string? Output { get; private set; }
    public long DurationMs => _stopwatch.ElapsedMilliseconds;
    public Dictionary<string, object> Metadata { get; } = new();

    public void Complete(bool success, string? errorMessageOrOutput = null)
    {
        _stopwatch.Stop();
        CompletedAt = DateTime.UtcNow;
        _status = success ? ExecutionStatus.Completed : ExecutionStatus.Failed;

        if (success)
            Output = errorMessageOrOutput;
        else
            ErrorMessage = errorMessageOrOutput;
    }

    public void AddMetadata(string key, object value)
    {
        Metadata[key] = value;
    }
}

/// <summary>
/// Summary of an execution trace for listings.
/// </summary>
public sealed class ExecutionTraceSummary
{
    public string ExecutionId { get; init; } = string.Empty;
    public string PipelineId { get; init; } = string.Empty;
    public string PipelineName { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public int? UserId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public ExecutionStatus Status { get; init; }
    public long DurationMs { get; init; }
    public int StepCount { get; init; }
    public int CompletedStepCount { get; init; }
    public int FailedStepCount { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Status of an execution or step.
/// </summary>
public enum ExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}
