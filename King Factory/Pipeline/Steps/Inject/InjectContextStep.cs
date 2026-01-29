using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Inject;

/// <summary>
/// Injects custom context or variables into the pipeline.
/// </summary>
public sealed class InjectContextStep : PipelineStepBase
{
    public override string TypeId => "inject.context";
    public override string DisplayName => "Inject Context";
    public override string Category => "Inject";
    public override string Description => "Injects custom context, project information, or variables into the pipeline.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "context",
            DisplayName = "Context Text",
            Type = StepParameterType.Template,
            Description = "Custom context text to inject into the system prompt"
        },
        new StepParameterDefinition
        {
            Name = "variables",
            DisplayName = "Variables",
            Type = StepParameterType.Object,
            Description = "Key-value pairs to set as pipeline variables"
        },
        new StepParameterDefinition
        {
            Name = "includeProjectPath",
            DisplayName = "Include Project Path",
            Type = StepParameterType.Boolean,
            Description = "Include the project path from input context",
            DefaultValue = true
        },
        new StepParameterDefinition
        {
            Name = "includeDateTime",
            DisplayName = "Include DateTime",
            Type = StepParameterType.Boolean,
            Description = "Include current date/time in context",
            DefaultValue = true
        }
    );

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var newContext = context;
        var contextParts = new List<string>();

        // Add custom context text
        var customContext = GetParameter<string>(config, "context");
        if (!string.IsNullOrWhiteSpace(customContext))
        {
            contextParts.Add(customContext);
        }

        // Add project path
        var includeProjectPath = GetParameter<bool>(config, "includeProjectPath", true);
        if (includeProjectPath && !string.IsNullOrWhiteSpace(context.Input.ProjectPath))
        {
            contextParts.Add($"**Current Project Path:** {context.Input.ProjectPath}");
        }

        // Add date/time
        var includeDateTime = GetParameter<bool>(config, "includeDateTime", true);
        if (includeDateTime)
        {
            contextParts.Add($"**Current Date/Time:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        }

        // Add any additional context from input
        if (context.Input.AdditionalContext != null)
        {
            foreach (var kvp in context.Input.AdditionalContext)
            {
                newContext = newContext.WithVariable($"input.{kvp.Key}", kvp.Value);
            }
        }

        // Inject variables
        var variables = GetParameter<Dictionary<string, object>>(config, "variables");
        if (variables != null)
        {
            foreach (var kvp in variables)
            {
                newContext = newContext.WithVariable(kvp.Key, kvp.Value);
            }
        }

        // Append context to system prompt
        if (contextParts.Count > 0)
        {
            var contextSection = "\n## Context\n" + string.Join("\n", contextParts);
            var currentPrompt = newContext.SystemPrompt ?? "";
            newContext = newContext.WithSystemPrompt(currentPrompt + contextSection);
        }

        return Task.FromResult(Success(newContext, $"Injected {contextParts.Count} context items"));
    }
}
