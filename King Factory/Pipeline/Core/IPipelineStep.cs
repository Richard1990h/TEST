using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// Interface for all pipeline steps.
/// Each step performs a single, well-defined operation on the pipeline context.
/// </summary>
public interface IPipelineStep
{
    /// <summary>
    /// Unique type identifier for this step (e.g., "inject.system-prompt", "llm.stream").
    /// </summary>
    string TypeId { get; }

    /// <summary>
    /// Human-readable name for this step type.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Category this step belongs to (e.g., "Inject", "LLM", "Tool", "Control", "Validate").
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Description of what this step does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether this step supports streaming output.
    /// </summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// Whether this step is async-only (cannot be executed synchronously).
    /// </summary>
    bool IsAsyncOnly { get; }

    /// <summary>
    /// JSON schema for the step's configuration parameters.
    /// Used for validation and UI generation.
    /// </summary>
    StepParameterSchema ParameterSchema { get; }

    /// <summary>
    /// Execute the step and return the result.
    /// </summary>
    /// <param name="context">The current pipeline context.</param>
    /// <param name="config">Step-specific configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result.</returns>
    Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken);

    /// <summary>
    /// Execute the step with streaming output.
    /// Only valid if SupportsStreaming is true.
    /// </summary>
    /// <param name="context">The current pipeline context.</param>
    /// <param name="config">Step-specific configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream of events from the step.</returns>
    IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validate the step configuration.
    /// </summary>
    /// <param name="config">Configuration to validate.</param>
    /// <returns>Validation result.</returns>
    StepValidationResult Validate(StepConfiguration config);
}

/// <summary>
/// Base class for pipeline steps with common functionality.
/// </summary>
public abstract class PipelineStepBase : IPipelineStep
{
    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public abstract string Category { get; }
    public abstract string Description { get; }
    public virtual bool SupportsStreaming => false;
    public virtual bool IsAsyncOnly => false;
    public virtual StepParameterSchema ParameterSchema => StepParameterSchema.Empty;

    public abstract Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken);

    public virtual async IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        PipelineContext context,
        StepConfiguration config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Default implementation: execute non-streaming and yield result
        var result = await ExecuteAsync(context, config, cancellationToken);

        if (result.Success)
        {
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.StepComplete,
                StepId = config.StepId,
                Context = result.Context
            };
        }
        else
        {
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Error,
                StepId = config.StepId,
                Content = result.ErrorMessage ?? "Step execution failed",
                Context = result.Context
            };
        }
    }

    public virtual StepValidationResult Validate(StepConfiguration config)
    {
        return StepValidationResult.Valid();
    }

    /// <summary>
    /// Helper to get a typed parameter from configuration.
    /// </summary>
    protected T? GetParameter<T>(StepConfiguration config, string name, T? defaultValue = default)
    {
        if (config.Parameters.TryGetValue(name, out var value))
        {
            if (value is T typed)
                return typed;

            if (value is JsonElement element)
            {
                try
                {
                    return element.Deserialize<T>();
                }
                catch
                {
                    return defaultValue;
                }
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// Helper to require a parameter.
    /// </summary>
    protected T RequireParameter<T>(StepConfiguration config, string name)
    {
        var value = GetParameter<T>(config, name);
        if (value == null)
            throw new StepConfigurationException($"Required parameter '{name}' is missing for step '{TypeId}'");
        return value;
    }

    /// <summary>
    /// Create a successful result with updated context.
    /// </summary>
    protected StepExecutionResult Success(PipelineContext context, string? output = null)
    {
        return StepExecutionResult.Succeeded(context, output);
    }

    /// <summary>
    /// Create a failed result.
    /// </summary>
    protected StepExecutionResult Failure(PipelineContext context, string errorMessage)
    {
        return StepExecutionResult.Failed(context, errorMessage);
    }
}

/// <summary>
/// Result of executing a pipeline step.
/// </summary>
public sealed class StepExecutionResult
{
    /// <summary>
    /// Whether the step executed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The updated pipeline context after step execution.
    /// </summary>
    public required PipelineContext Context { get; init; }

    /// <summary>
    /// Output from the step (for display/logging).
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Duration of step execution.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Additional metadata from the step.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    public static StepExecutionResult Succeeded(PipelineContext context, string? output = null, IReadOnlyDictionary<string, object>? metadata = null)
    {
        return new StepExecutionResult
        {
            Success = true,
            Context = context,
            Output = output,
            Metadata = metadata
        };
    }

    public static StepExecutionResult Failed(PipelineContext context, string errorMessage)
    {
        return new StepExecutionResult
        {
            Success = false,
            Context = context.WithStop(errorMessage),
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Configuration for a step instance.
/// </summary>
public sealed class StepConfiguration
{
    /// <summary>
    /// Unique ID of this step instance within the pipeline.
    /// </summary>
    public required string StepId { get; init; }

    /// <summary>
    /// The step type ID (e.g., "inject.system-prompt").
    /// </summary>
    public required string TypeId { get; init; }

    /// <summary>
    /// Step-specific parameters.
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Optional description override for this instance.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this step is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Timeout for this step in milliseconds. 0 = no timeout.
    /// </summary>
    public int TimeoutMs { get; init; }

    /// <summary>
    /// Number of retry attempts on failure.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Whether to continue pipeline execution if this step fails.
    /// </summary>
    public bool ContinueOnError { get; init; }

    /// <summary>
    /// Condition expression for conditional execution.
    /// </summary>
    public string? Condition { get; init; }
}

/// <summary>
/// Schema definition for step parameters.
/// </summary>
public sealed class StepParameterSchema
{
    public IReadOnlyList<StepParameterDefinition> Parameters { get; init; } = Array.Empty<StepParameterDefinition>();

    public static StepParameterSchema Empty => new() { Parameters = Array.Empty<StepParameterDefinition>() };

    public static StepParameterSchema Create(params StepParameterDefinition[] parameters)
    {
        return new StepParameterSchema { Parameters = parameters };
    }
}

/// <summary>
/// Definition of a single step parameter.
/// </summary>
public sealed class StepParameterDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required StepParameterType Type { get; init; }
    public string? Description { get; init; }
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
    public IReadOnlyList<object>? AllowedValues { get; init; }
    public object? MinValue { get; init; }
    public object? MaxValue { get; init; }
}

/// <summary>
/// Types of step parameters.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepParameterType
{
    String,
    Integer,
    Float,
    Boolean,
    StringArray,
    Object,
    Enum,
    Template,
    Code
}

/// <summary>
/// Result of validating a step configuration.
/// </summary>
public sealed class StepValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static StepValidationResult Valid() => new() { IsValid = true };

    public static StepValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };

    public static StepValidationResult WithWarnings(params string[] warnings) => new()
    {
        IsValid = true,
        Warnings = warnings
    };
}

/// <summary>
/// Events emitted during streaming pipeline execution.
/// </summary>
public sealed class PipelineStreamEvent
{
    /// <summary>
    /// Type of the event.
    /// </summary>
    public PipelineStreamEventType Type { get; init; }

    /// <summary>
    /// Step ID that generated this event.
    /// </summary>
    public string? StepId { get; init; }

    /// <summary>
    /// Content of the event (e.g., token text, status message).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Current pipeline context.
    /// </summary>
    public PipelineContext? Context { get; init; }

    /// <summary>
    /// Tool name if this is a tool event.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Tool arguments if this is a tool call event.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ToolArguments { get; init; }

    /// <summary>
    /// Whether tool execution succeeded (for tool result events).
    /// </summary>
    public bool? ToolSuccess { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Types of pipeline stream events.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineStreamEventType
{
    /// <summary>
    /// Pipeline execution started.
    /// </summary>
    Started,

    /// <summary>
    /// A step started executing.
    /// </summary>
    StepStarted,

    /// <summary>
    /// A step completed successfully.
    /// </summary>
    StepComplete,

    /// <summary>
    /// A token was generated (streaming text output).
    /// </summary>
    Token,

    /// <summary>
    /// A tool is being called.
    /// </summary>
    ToolCall,

    /// <summary>
    /// A tool execution completed.
    /// </summary>
    ToolResult,

    /// <summary>
    /// Progress update.
    /// </summary>
    Progress,

    /// <summary>
    /// Warning message.
    /// </summary>
    Warning,

    /// <summary>
    /// Error occurred.
    /// </summary>
    Error,

    /// <summary>
    /// Pipeline execution completed.
    /// </summary>
    Complete
}

/// <summary>
/// Exception thrown for step configuration errors.
/// </summary>
public class StepConfigurationException : Exception
{
    public StepConfigurationException(string message) : base(message) { }
    public StepConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown during step execution.
/// </summary>
public class StepExecutionException : Exception
{
    public string StepId { get; }
    public string StepType { get; }

    public StepExecutionException(string stepId, string stepType, string message)
        : base(message)
    {
        StepId = stepId;
        StepType = stepType;
    }

    public StepExecutionException(string stepId, string stepType, string message, Exception innerException)
        : base(message, innerException)
    {
        StepId = stepId;
        StepType = stepType;
    }
}
