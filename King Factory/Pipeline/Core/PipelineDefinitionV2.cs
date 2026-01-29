using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// V2 Pipeline Definition - A declarative, data-driven pipeline schema.
/// Pipelines are data, not code.
/// </summary>
public sealed class PipelineDefinitionV2
{
    /// <summary>
    /// Unique identifier for this pipeline.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this pipeline does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Semantic version of this pipeline.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Current status of this pipeline.
    /// </summary>
    public PipelineStatusV2 Status { get; set; } = PipelineStatusV2.Draft;

    /// <summary>
    /// Whether this is the primary (default) pipeline.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// When this pipeline was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this pipeline was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The steps in this pipeline.
    /// </summary>
    public List<PipelineStepV2> Steps { get; set; } = new();

    /// <summary>
    /// Global configuration for this pipeline.
    /// </summary>
    public PipelineConfigV2 Config { get; set; } = new();

    /// <summary>
    /// Metadata for this pipeline.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Tags for categorization and filtering.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Create a deep clone of this pipeline.
    /// </summary>
    public PipelineDefinitionV2 Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<PipelineDefinitionV2>(json) ?? new PipelineDefinitionV2();
    }

    /// <summary>
    /// Validate the pipeline definition.
    /// </summary>
    public PipelineValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Pipeline name is required");

        if (Steps.Count == 0)
            errors.Add("Pipeline must have at least one step");

        // Check for duplicate step IDs
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                errors.Add("All steps must have an ID");
            }
            else if (!stepIds.Add(step.Id))
            {
                errors.Add($"Duplicate step ID: {step.Id}");
            }

            if (string.IsNullOrWhiteSpace(step.Type))
            {
                errors.Add($"Step {step.Id ?? "(unnamed)"} must have a type");
            }
        }

        // Check for invalid dependency references
        foreach (var step in Steps)
        {
            if (step.DependsOn == null) continue;

            foreach (var dep in step.DependsOn)
            {
                if (!stepIds.Contains(dep))
                {
                    errors.Add($"Step {step.Id} depends on unknown step: {dep}");
                }

                if (string.Equals(dep, step.Id, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Step {step.Id} cannot depend on itself");
                }
            }
        }

        // Warnings
        if (Config.MaxOutputTokens > 8192)
            warnings.Add("MaxOutputTokens > 8192 may cause issues with some models");

        if (Config.Temperature > 1.5f)
            warnings.Add("Temperature > 1.5 may produce inconsistent results");

        return new PipelineValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}

/// <summary>
/// Status of a V2 pipeline.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineStatusV2
{
    /// <summary>
    /// Pipeline is in draft mode, not ready for production.
    /// </summary>
    Draft,

    /// <summary>
    /// Pipeline is active and can be executed.
    /// </summary>
    Active,

    /// <summary>
    /// Pipeline is disabled but not deleted.
    /// </summary>
    Inactive,

    /// <summary>
    /// Pipeline is archived (historical).
    /// </summary>
    Archived
}

/// <summary>
/// A step in a V2 pipeline.
/// </summary>
public sealed class PipelineStepV2
{
    /// <summary>
    /// Unique identifier for this step within the pipeline.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The step type ID (e.g., "inject.system-prompt", "llm.stream-with-tools").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this step does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Step IDs that must complete before this step can execute.
    /// </summary>
    public List<string>? DependsOn { get; set; }

    /// <summary>
    /// Step-specific parameters.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Execution order hint (used when dependencies don't fully determine order).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether this step is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout in milliseconds. 0 = no timeout.
    /// </summary>
    public int TimeoutMs { get; set; }

    /// <summary>
    /// Number of retry attempts on failure.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Whether to continue pipeline execution if this step fails.
    /// </summary>
    public bool ContinueOnError { get; set; }

    /// <summary>
    /// Condition expression for conditional execution.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Convert to StepConfiguration for execution.
    /// </summary>
    public StepConfiguration ToConfiguration()
    {
        return new StepConfiguration
        {
            StepId = Id,
            TypeId = Type,
            Description = Description,
            Parameters = Parameters,
            Enabled = Enabled,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            ContinueOnError = ContinueOnError,
            Condition = Condition
        };
    }
}

/// <summary>
/// Global configuration for a V2 pipeline.
/// </summary>
public sealed class PipelineConfigV2
{
    /// <summary>
    /// Keywords that trigger this pipeline. If any keyword is found in the user message,
    /// this pipeline will be used.
    /// </summary>
    public List<string> TriggerKeywords { get; set; } = new();

    /// <summary>
    /// Regular expression patterns that trigger this pipeline.
    /// </summary>
    public List<string> TriggerPatterns { get; set; } = new();

    /// <summary>
    /// Intent types that trigger this pipeline.
    /// </summary>
    public List<string> TriggerIntents { get; set; } = new();

    /// <summary>
    /// Maximum context tokens for LLM.
    /// </summary>
    public int MaxContextTokens { get; set; } = 4096;

    /// <summary>
    /// Maximum output tokens for LLM.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Temperature for LLM generation.
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Maximum tool execution iterations.
    /// </summary>
    public int MaxToolIterations { get; set; } = 10;

    /// <summary>
    /// Global timeout for the entire pipeline in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; }

    /// <summary>
    /// Whether to enable parallel step execution when dependencies allow.
    /// </summary>
    public bool EnableParallelExecution { get; set; } = false;

    /// <summary>
    /// Default system prompt if not injected by a step.
    /// </summary>
    public string? DefaultSystemPrompt { get; set; }

    /// <summary>
    /// Tools enabled for this pipeline.
    /// </summary>
    public List<string> EnabledTools { get; set; } = new();

    /// <summary>
    /// Whether to enable execution tracing.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Whether to store execution logs.
    /// </summary>
    public bool EnableExecutionLogs { get; set; } = true;

    /// <summary>
    /// Priority of this pipeline (higher = more important).
    /// </summary>
    public int Priority { get; set; } = 0;
}

/// <summary>
/// Result of validating a pipeline definition.
/// </summary>
public sealed class PipelineValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Static factory for creating default pipelines.
/// </summary>
public static class DefaultPipelines
{
    /// <summary>
    /// Create the standard chat pipeline.
    /// </summary>
    public static PipelineDefinitionV2 CreateStandardChat()
    {
        return new PipelineDefinitionV2
        {
            Id = "standard-chat",
            Name = "Standard Chat",
            Description = "Standard conversational AI with tool support",
            Version = "1.0.0",
            Status = PipelineStatusV2.Active,
            IsPrimary = true,
            Steps = new List<PipelineStepV2>
            {
                new()
                {
                    Id = "inject-system",
                    Type = "inject.system-prompt",
                    Order = 1
                },
                new()
                {
                    Id = "inject-conv",
                    Type = "inject.conversation",
                    DependsOn = new List<string> { "inject-system" },
                    Order = 2
                },
                new()
                {
                    Id = "inject-tools",
                    Type = "inject.tools",
                    DependsOn = new List<string> { "inject-conv" },
                    Order = 3
                },
                new()
                {
                    Id = "generate",
                    Type = "llm.stream-with-tools",
                    DependsOn = new List<string> { "inject-tools" },
                    Order = 4
                },
                new()
                {
                    Id = "validate",
                    Type = "validate.output",
                    DependsOn = new List<string> { "generate" },
                    Order = 5
                }
            },
            Config = new PipelineConfigV2
            {
                MaxContextTokens = 4096,
                MaxOutputTokens = 2048,
                Temperature = 0.7f,
                MaxToolIterations = 10,
                EnableTracing = true
            }
        };
    }

    /// <summary>
    /// Create the code generation pipeline.
    /// </summary>
    public static PipelineDefinitionV2 CreateCodeGeneration()
    {
        return new PipelineDefinitionV2
        {
            Id = "code-generation",
            Name = "Code Generation",
            Description = "Specialized pipeline for code writing tasks",
            Version = "1.0.0",
            Status = PipelineStatusV2.Active,
            Steps = new List<PipelineStepV2>
            {
                new()
                {
                    Id = "inject-system",
                    Type = "inject.system-prompt",
                    Parameters = new Dictionary<string, object>
                    {
                        ["mode"] = "code"
                    },
                    Order = 1
                },
                new()
                {
                    Id = "inject-conv",
                    Type = "inject.conversation",
                    DependsOn = new List<string> { "inject-system" },
                    Order = 2
                },
                new()
                {
                    Id = "classify",
                    Type = "llm.classify",
                    DependsOn = new List<string> { "inject-conv" },
                    Parameters = new Dictionary<string, object>
                    {
                        ["categories"] = new[] { "write", "fix", "explain", "review" },
                        ["outputVariable"] = "codeIntent"
                    },
                    Order = 3
                },
                new()
                {
                    Id = "generate-code",
                    Type = "llm.stream",
                    DependsOn = new List<string> { "classify" },
                    Parameters = new Dictionary<string, object>
                    {
                        ["codeMode"] = true
                    },
                    Order = 4
                },
                new()
                {
                    Id = "validate-code",
                    Type = "validate.code",
                    DependsOn = new List<string> { "generate-code" },
                    Order = 5
                }
            },
            Config = new PipelineConfigV2
            {
                TriggerIntents = new List<string> { "CodeWrite", "CodeEdit", "CodeExplain" },
                MaxContextTokens = 8192,
                MaxOutputTokens = 4096,
                Temperature = 0.3f,
                EnableTracing = true
            },
            Tags = new List<string> { "code", "development" }
        };
    }

    /// <summary>
    /// Create a simple chat pipeline without tools.
    /// </summary>
    public static PipelineDefinitionV2 CreateSimpleChat()
    {
        return new PipelineDefinitionV2
        {
            Id = "simple-chat",
            Name = "Simple Chat",
            Description = "Simple conversational AI without tools",
            Version = "1.0.0",
            Status = PipelineStatusV2.Active,
            Steps = new List<PipelineStepV2>
            {
                new()
                {
                    Id = "inject-system",
                    Type = "inject.system-prompt",
                    Order = 1
                },
                new()
                {
                    Id = "inject-conv",
                    Type = "inject.conversation",
                    DependsOn = new List<string> { "inject-system" },
                    Order = 2
                },
                new()
                {
                    Id = "generate",
                    Type = "llm.stream",
                    DependsOn = new List<string> { "inject-conv" },
                    Order = 3
                }
            },
            Config = new PipelineConfigV2
            {
                MaxContextTokens = 4096,
                MaxOutputTokens = 2048,
                Temperature = 0.7f,
                EnableTracing = true
            },
            Tags = new List<string> { "chat", "simple" }
        };
    }

    /// <summary>
    /// Get all default pipelines.
    /// </summary>
    public static IReadOnlyList<PipelineDefinitionV2> GetAll()
    {
        return new[]
        {
            CreateStandardChat(),
            CreateCodeGeneration(),
            CreateSimpleChat()
        };
    }
}
