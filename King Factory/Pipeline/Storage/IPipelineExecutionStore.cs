using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Storage;

/// <summary>
/// Storage interface for pipeline execution logs and metrics.
/// </summary>
public interface IPipelineExecutionStore
{
    /// <summary>
    /// Record a new execution.
    /// </summary>
    Task<string> BeginExecutionAsync(
        string pipelineId,
        string? conversationId,
        int? userId,
        string? inputSummary,
        int stepCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update execution completion.
    /// </summary>
    Task CompleteExecutionAsync(
        string executionId,
        bool success,
        string? errorMessage,
        long durationMs,
        int completedSteps,
        string? outputSummary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a step execution.
    /// </summary>
    Task RecordStepAsync(
        string executionId,
        string stepId,
        string stepType,
        int stepOrder,
        bool success,
        long durationMs,
        string? inputJson,
        string? outputJson,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get executions with filtering.
    /// </summary>
    Task<PagedResult<ExecutionSummary>> GetExecutionsAsync(
        ExecutionFilter filter,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get execution details including step logs.
    /// </summary>
    Task<ExecutionDetails?> GetExecutionDetailsAsync(
        string executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregated metrics.
    /// </summary>
    Task<PipelineMetrics> GetMetricsAsync(
        string? pipelineId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update metrics for a pipeline.
    /// </summary>
    Task UpdateMetricsAsync(
        string pipelineId,
        DateTime date,
        bool success,
        long durationMs,
        int stepsExecuted,
        int toolCalls,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter for querying executions.
/// </summary>
public sealed class ExecutionFilter
{
    public string? PipelineId { get; init; }
    public int? UserId { get; init; }
    public string? Status { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// Paged result wrapper.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// Summary of an execution for listings.
/// </summary>
public sealed class ExecutionSummary
{
    public required string Id { get; init; }
    public required string PipelineId { get; init; }
    public string? PipelineName { get; init; }
    public string? ConversationId { get; init; }
    public int? UserId { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public int StepCount { get; init; }
    public int CompletedStepCount { get; init; }
    public string? InputSummary { get; init; }
}

/// <summary>
/// Full execution details including step logs.
/// </summary>
public sealed class ExecutionDetails
{
    public required string Id { get; init; }
    public required string PipelineId { get; init; }
    public string? PipelineName { get; init; }
    public string? ConversationId { get; init; }
    public int? UserId { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public int StepCount { get; init; }
    public int CompletedStepCount { get; init; }
    public string? ErrorMessage { get; init; }
    public string? InputSummary { get; init; }
    public string? OutputSummary { get; init; }
    public IReadOnlyList<StepLog> Steps { get; init; } = Array.Empty<StepLog>();
}

/// <summary>
/// Log of a single step execution.
/// </summary>
public sealed class StepLog
{
    public required string Id { get; init; }
    public required string StepId { get; init; }
    public required string StepType { get; init; }
    public int StepOrder { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public string? InputJson { get; init; }
    public string? OutputJson { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Aggregated pipeline metrics.
/// </summary>
public sealed class PipelineMetrics
{
    public int TotalExecutions { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessCount / TotalExecutions : 0;
    public long? AvgDurationMs { get; init; }
    public long? MinDurationMs { get; init; }
    public long? MaxDurationMs { get; init; }
    public int TotalStepsExecuted { get; init; }
    public int TotalToolCalls { get; init; }
    public IReadOnlyList<DailyMetric> DailyMetrics { get; init; } = Array.Empty<DailyMetric>();
}

/// <summary>
/// Metrics for a single day.
/// </summary>
public sealed class DailyMetric
{
    public DateTime Date { get; init; }
    public int TotalExecutions { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public long? AvgDurationMs { get; init; }
}
