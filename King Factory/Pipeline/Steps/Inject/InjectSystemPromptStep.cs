using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Prompts;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Inject;

/// <summary>
/// Injects the system prompt into the pipeline context.
/// </summary>
public sealed class InjectSystemPromptStep : PipelineStepBase
{
    private readonly ISystemPrompts _systemPrompts;

    public override string TypeId => "inject.system-prompt";
    public override string DisplayName => "Inject System Prompt";
    public override string Category => "Inject";
    public override string Description => "Injects the system prompt into the context. This sets up the AI's personality, capabilities, and constraints.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "mode",
            DisplayName = "Mode",
            Type = StepParameterType.Enum,
            Description = "The prompt mode to use",
            AllowedValues = new object[] { "default", "code", "planning", "fix" },
            DefaultValue = "default"
        },
        new StepParameterDefinition
        {
            Name = "customPrompt",
            DisplayName = "Custom Prompt",
            Type = StepParameterType.Template,
            Description = "Optional custom system prompt to use instead of the default"
        },
        new StepParameterDefinition
        {
            Name = "includeTools",
            DisplayName = "Include Tools",
            Type = StepParameterType.Boolean,
            Description = "Whether to include tool instructions in the prompt",
            DefaultValue = true
        }
    );

    public InjectSystemPromptStep(ISystemPrompts systemPrompts)
    {
        _systemPrompts = systemPrompts;
    }

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var customPrompt = GetParameter<string>(config, "customPrompt");

        if (!string.IsNullOrWhiteSpace(customPrompt))
        {
            // Use custom prompt
            var updatedContext = context.WithSystemPrompt(customPrompt);
            return Task.FromResult(Success(updatedContext, "Custom system prompt injected"));
        }

        // Build system prompt based on mode
        var mode = GetParameter<string>(config, "mode", "default");
        var includeTools = GetParameter<bool>(config, "includeTools", true);

        var options = new PromptBuildOptions
        {
            IncludeTools = includeTools && context.Tools.Count > 0,
            IncludePlanning = mode == "planning",
            IncludeCodeMode = mode == "code",
            IncludeFixMode = mode == "fix"
        };

        var systemPrompt = _systemPrompts.Build(options);
        var newContext = context.WithSystemPrompt(systemPrompt);

        return Task.FromResult(Success(newContext, $"System prompt injected (mode: {mode})"));
    }
}
