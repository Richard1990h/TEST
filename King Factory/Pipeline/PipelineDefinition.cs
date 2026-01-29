using System.Text.Json.Serialization;

namespace LittleHelperAI.KingFactory.Pipeline;

/// <summary>
/// Represents a complete pipeline definition.
/// </summary>
public class PipelineDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public PipelineStatus Status { get; set; } = PipelineStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<PipelineStep> Steps { get; set; } = new();
    public PipelineConfig Config { get; set; } = new();
}

/// <summary>
/// Pipeline status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineStatus
{
    Draft,
    Active,
    Archived
}

/// <summary>
/// A single step in the pipeline.
/// </summary>
public class PipelineStep
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? DependsOn { get; set; }
    public Dictionary<string, object>? Config { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Pipeline configuration options.
/// </summary>
public class PipelineConfig
{
    /// <summary>
    /// Keywords that trigger this pipeline. If any keyword is found in the user message,
    /// this pipeline will be used instead of the primary pipeline.
    /// </summary>
    public List<string> TriggerKeywords { get; set; } = new();

    /// <summary>
    /// Prompts to include in this pipeline.
    /// </summary>
    public List<string> IncludedPrompts { get; set; } = new();

    /// <summary>
    /// Tools available in this pipeline.
    /// </summary>
    public List<string> EnabledTools { get; set; } = new();

    /// <summary>
    /// Whether to inject conversation context.
    /// </summary>
    public bool InjectConversation { get; set; } = true;

    /// <summary>
    /// Maximum context tokens.
    /// </summary>
    public int MaxContextTokens { get; set; } = 4096;

    /// <summary>
    /// Maximum tool iterations.
    /// </summary>
    public int MaxToolIterations { get; set; } = 10;

    /// <summary>
    /// Temperature for generation.
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Maximum output tokens.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Whether to enable planning mode for complex tasks.
    /// </summary>
    public bool EnablePlanning { get; set; } = true;

    /// <summary>
    /// Whether to enable validation of outputs.
    /// </summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>
    /// Optional prompt overrides for this pipeline.
    /// </summary>
    public PromptOverrides? PromptOverrides { get; set; }
}

/// <summary>
/// Prompt overrides for a pipeline.
/// </summary>
public class PromptOverrides
{
    public string? SystemPrompt { get; set; }
    public string? DeveloperPrompt { get; set; }
    public string? ToolsPrompt { get; set; }
    public string? PlanningPrompt { get; set; }
    public string? ValidationPrompt { get; set; }
}

/// <summary>
/// Pipeline step types.
/// </summary>
public static class PipelineStepTypes
{
    // Injection steps
    public const string InjectConversation = "inject.conversation";
    public const string InjectSystemPrompt = "inject.system-prompt";
    public const string InjectCorePrompt = "inject.core-prompt";
    public const string InjectToolsPrompt = "inject.tools-prompt";
    public const string InjectPlanningPrompt = "inject.planning-prompt";
    public const string InjectValidationPrompt = "inject.validation-prompt";
    public const string InjectProjectContext = "inject.project-context";

    // Processing steps
    public const string LlmGenerate = "llm.generate";
    public const string LlmStream = "llm.stream";

    // Tool steps
    public const string ToolParse = "tool.parse";
    public const string ToolExecute = "tool.execute";
    public const string ToolLoop = "tool.loop";

    // Analysis steps
    public const string AnalyzeIntent = "analyze.intent";
    public const string AnalyzeScope = "analyze.scope";

    // Validation steps
    public const string ValidateOutput = "validate.output";
    public const string ValidateCode = "validate.code";

    // Transform steps
    public const string TransformWriteFile = "transform.write-file";
    public const string TransformApplyPatch = "transform.apply-patch";

    // Build steps
    public const string BuildDetect = "build.detect-system";
    public const string BuildExecute = "build.execute";
    public const string BuildRestore = "build.restore";

    // Memory steps
    public const string MemoryLoad = "memory.load";
    public const string MemorySave = "memory.save";
}
