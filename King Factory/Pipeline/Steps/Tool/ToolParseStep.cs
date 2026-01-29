using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Tools;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Tool;

/// <summary>
/// Parses tool calls from LLM response text.
/// </summary>
public sealed class ToolParseStep : PipelineStepBase
{
    private readonly IToolRouter _toolRouter;

    public override string TypeId => "tool.parse";
    public override string DisplayName => "Parse Tool Call";
    public override string Category => "Tool";
    public override string Description => "Parses tool calls from the LLM response and stores them for execution.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "sourceVariable",
            DisplayName = "Source Variable",
            Type = StepParameterType.String,
            Description = "Variable containing the text to parse (default: uses response text)"
        },
        new StepParameterDefinition
        {
            Name = "outputVariable",
            DisplayName = "Output Variable",
            Type = StepParameterType.String,
            Description = "Variable to store the parsed tool call",
            DefaultValue = "parsedToolCall"
        }
    );

    public ToolParseStep(IToolRouter toolRouter)
    {
        _toolRouter = toolRouter;
    }

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var sourceVariable = GetParameter<string>(config, "sourceVariable");
        var outputVariable = GetParameter<string>(config, "outputVariable", "parsedToolCall")!;

        // Get text to parse
        string textToParse;
        if (!string.IsNullOrEmpty(sourceVariable))
        {
            textToParse = context.GetVariable<string>(sourceVariable) ?? "";
        }
        else
        {
            textToParse = context.ResponseText;
        }

        if (string.IsNullOrEmpty(textToParse))
        {
            var noCallContext = context.WithVariable(outputVariable, null!);
            return Task.FromResult(Success(noCallContext, "No text to parse"));
        }

        // Parse tool call
        var toolCall = _toolRouter.ParseToolCall(textToParse);

        if (toolCall == null)
        {
            var noCallContext = context.WithVariable(outputVariable, null!);
            return Task.FromResult(Success(noCallContext, "No tool call found"));
        }

        // Store parsed tool call
        var newContext = context
            .WithVariable(outputVariable, toolCall)
            .WithVariable($"{outputVariable}.toolName", toolCall.ToolName)
            .WithVariable($"{outputVariable}.arguments", toolCall.Arguments);

        return Task.FromResult(Success(newContext, $"Parsed tool call: {toolCall.ToolName}"));
    }
}
