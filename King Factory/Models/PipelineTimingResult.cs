using System.Text.Json.Serialization;

namespace LittleHelperAI.KingFactory.Models;

/// <summary>
/// Timing data for a single pipeline stage.
/// </summary>
public class StageTimingData
{
    /// <summary>
    /// Name of the stage.
    /// </summary>
    public string StageName { get; set; } = string.Empty;

    /// <summary>
    /// When the stage started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the stage completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration of the stage.
    /// </summary>
    public TimeSpan Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : DateTime.UtcNow - StartedAt;

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public long DurationMs => (long)Duration.TotalMilliseconds;

    /// <summary>
    /// Whether the stage completed successfully.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if stage failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Additional metadata for the stage.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Complete timing result for a pipeline execution.
/// </summary>
public class PipelineTimingResult
{
    /// <summary>
    /// Unique identifier for this pipeline execution.
    /// </summary>
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Pipeline identifier.
    /// </summary>
    public string PipelineId { get; set; } = string.Empty;

    /// <summary>
    /// Pipeline name.
    /// </summary>
    public string PipelineName { get; set; } = string.Empty;

    /// <summary>
    /// Conversation identifier.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// When the pipeline execution started.
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the pipeline execution completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Total duration of the pipeline.
    /// </summary>
    public TimeSpan TotalDuration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : DateTime.UtcNow - StartedAt;

    /// <summary>
    /// Total duration in milliseconds.
    /// </summary>
    public long TotalDurationMs => (long)TotalDuration.TotalMilliseconds;

    /// <summary>
    /// Whether the pipeline completed successfully.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Timing data for each stage.
    /// </summary>
    public List<StageTimingData> Stages { get; set; } = new();

    /// <summary>
    /// Time to first token in milliseconds.
    /// </summary>
    public long? TtftMs { get; set; }

    /// <summary>
    /// Tokens per second during generation.
    /// </summary>
    public double? TokensPerSecond { get; set; }

    /// <summary>
    /// Total tokens generated.
    /// </summary>
    public int? TotalTokens { get; set; }

    /// <summary>
    /// Number of tool calls made.
    /// </summary>
    public int ToolCallCount { get; set; }

    /// <summary>
    /// Total time spent in tool execution.
    /// </summary>
    public long ToolExecutionMs { get; set; }

    /// <summary>
    /// Get timing for a specific stage.
    /// </summary>
    public StageTimingData? GetStage(string stageName)
    {
        return Stages.FirstOrDefault(s =>
            s.StageName.Equals(stageName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Convert to metadata dictionary for FactoryOutput.
    /// </summary>
    public Dictionary<string, object> ToMetadata()
    {
        var metadata = new Dictionary<string, object>
        {
            ["executionId"] = ExecutionId,
            ["pipelineId"] = PipelineId,
            ["totalDurationMs"] = TotalDurationMs,
            ["success"] = Success,
            ["toolCallCount"] = ToolCallCount
        };

        if (TtftMs.HasValue)
            metadata["ttftMs"] = TtftMs.Value;

        if (TokensPerSecond.HasValue)
            metadata["tokensPerSecond"] = TokensPerSecond.Value;

        if (TotalTokens.HasValue)
            metadata["totalTokens"] = TotalTokens.Value;

        var stageTiming = new Dictionary<string, long>();
        foreach (var stage in Stages)
        {
            stageTiming[stage.StageName] = stage.DurationMs;
        }
        metadata["stageTiming"] = stageTiming;

        return metadata;
    }
}

/// <summary>
/// Well-known pipeline stage names.
/// </summary>
public static class PipelineStages
{
    public const string Inject = "inject";
    public const string Analyze = "analyze";
    public const string Generate = "generate";
    public const string Tools = "tools";
    public const string Validate = "validate";
    public const string RequirementsExtraction = "requirements";
    public const string Summarization = "summarization";
    public const string ToolExecution = "tool_execution";
}
