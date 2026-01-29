using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Tools;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Inject;

/// <summary>
/// Injects available tools into the pipeline context.
/// </summary>
public sealed class InjectToolsStep : PipelineStepBase
{
    private readonly IToolRegistry _toolRegistry;

    public override string TypeId => "inject.tools";
    public override string DisplayName => "Inject Tools";
    public override string Category => "Inject";
    public override string Description => "Injects available tools into the context for LLM tool use.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "tools",
            DisplayName = "Tools",
            Type = StepParameterType.StringArray,
            Description = "List of tool names to include. Leave empty for all tools."
        },
        new StepParameterDefinition
        {
            Name = "excludeTools",
            DisplayName = "Exclude Tools",
            Type = StepParameterType.StringArray,
            Description = "List of tool names to exclude."
        }
    );

    public InjectToolsStep(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var includeTools = GetParameter<string[]>(config, "tools");
        var excludeTools = GetParameter<string[]>(config, "excludeTools") ?? Array.Empty<string>();

        // Get tools based on configuration
        IEnumerable<ITool> tools;

        if (includeTools != null && includeTools.Length > 0)
        {
            // Only include specified tools
            tools = includeTools
                .Select(name => _toolRegistry.GetTool(name))
                .Where(t => t != null)
                .Cast<ITool>();
        }
        else
        {
            // Use all tools from registry
            tools = _toolRegistry.GetAllTools();
        }

        // Exclude specified tools
        var excludeSet = new HashSet<string>(excludeTools, StringComparer.OrdinalIgnoreCase);
        tools = tools.Where(t => !excludeSet.Contains(t.Name));

        // Also check pipeline config for enabled tools
        var pipelineEnabledTools = context.Pipeline.Config.EnabledTools;
        if (pipelineEnabledTools.Count > 0)
        {
            var enabledSet = new HashSet<string>(pipelineEnabledTools, StringComparer.OrdinalIgnoreCase);
            tools = tools.Where(t => enabledSet.Contains(t.Name));
        }

        var toolList = tools.ToList();
        var newContext = context.WithTools(toolList);

        // Add tool manifest to system prompt if we have tools
        if (toolList.Count > 0)
        {
            var manifest = _toolRegistry.GetToolManifest();
            var currentPrompt = context.SystemPrompt ?? "";

            if (!currentPrompt.Contains("Available Tools"))
            {
                newContext = newContext.WithSystemPrompt(currentPrompt + "\n\n" + manifest);
            }
        }

        return Task.FromResult(Success(newContext, $"Injected {toolList.Count} tools"));
    }
}
