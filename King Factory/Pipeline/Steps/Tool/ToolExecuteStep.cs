using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Tools;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Tool;

/// <summary>
/// Executes a parsed tool call.
/// </summary>
public sealed class ToolExecuteStep : PipelineStepBase
{
    private readonly IToolRouter _toolRouter;

    public override string TypeId => "tool.execute";
    public override string DisplayName => "Execute Tool";
    public override string Category => "Tool";
    public override string Description => "Executes a tool call that was previously parsed and stores the result.";
    public override bool SupportsStreaming => true;

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "toolCallVariable",
            DisplayName = "Tool Call Variable",
            Type = StepParameterType.String,
            Description = "Variable containing the tool call to execute",
            DefaultValue = "parsedToolCall"
        },
        new StepParameterDefinition
        {
            Name = "outputVariable",
            DisplayName = "Output Variable",
            Type = StepParameterType.String,
            Description = "Variable to store the tool result",
            DefaultValue = "toolResult"
        },
        new StepParameterDefinition
        {
            Name = "addToConversation",
            DisplayName = "Add to Conversation",
            Type = StepParameterType.Boolean,
            Description = "Add tool call and result to conversation messages",
            DefaultValue = true
        }
    );

    public ToolExecuteStep(IToolRouter toolRouter)
    {
        _toolRouter = toolRouter;
    }

    public override async Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var toolCallVariable = GetParameter<string>(config, "toolCallVariable", "parsedToolCall")!;
        var outputVariable = GetParameter<string>(config, "outputVariable", "toolResult")!;
        var addToConversation = GetParameter<bool>(config, "addToConversation", true);

        // Get tool call from variable
        var toolCall = context.GetVariable<ToolCall>(toolCallVariable);
        if (toolCall == null)
        {
            return Success(context, "No tool call to execute");
        }

        // Execute the tool
        var result = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);

        // Store result
        var newContext = context
            .WithVariable(outputVariable, result)
            .WithVariable($"{outputVariable}.success", result.Success)
            .WithVariable($"{outputVariable}.output", result.Output ?? "")
            .WithVariable($"{outputVariable}.error", result.Error ?? "")
            .WithToolResult(new ToolExecutionResult
            {
                ToolName = toolCall.ToolName,
                ToolCallId = toolCall.Id,
                Success = result.Success,
                Output = result.Output,
                Error = result.Error,
                Duration = result.ExecutionTime,
                Arguments = toolCall.Arguments
            });

        // Add to conversation if requested
        if (addToConversation)
        {
            newContext = newContext
                .WithMessage(new ChatMessage
                {
                    Role = "assistant",
                    Content = $"I'm using the {toolCall.ToolName} tool.",
                    ToolCalls = new List<ToolCall> { toolCall }
                })
                .WithMessage(new ChatMessage
                {
                    Role = "tool",
                    Content = result.Success
                        ? $"Tool '{toolCall.ToolName}' succeeded:\n{result.Output}"
                        : $"Tool '{toolCall.ToolName}' failed:\n{result.Error}",
                    ToolCallId = toolCall.Id
                });
        }

        return Success(newContext, result.Success
            ? $"Tool {toolCall.ToolName} executed successfully"
            : $"Tool {toolCall.ToolName} failed: {result.Error}");
    }

    public override async IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        PipelineContext context,
        StepConfiguration config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolCallVariable = GetParameter<string>(config, "toolCallVariable", "parsedToolCall")!;

        var toolCall = context.GetVariable<ToolCall>(toolCallVariable);
        if (toolCall == null)
        {
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.StepComplete,
                StepId = config.StepId,
                Content = "No tool call to execute",
                Context = context
            };
            yield break;
        }

        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.ToolCall,
            StepId = config.StepId,
            ToolName = toolCall.ToolName,
            ToolArguments = toolCall.Arguments,
            Context = context
        };

        var result = await ExecuteAsync(context, config, cancellationToken);

        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.ToolResult,
            StepId = config.StepId,
            ToolName = toolCall.ToolName,
            ToolSuccess = result.Success,
            Content = result.Output,
            Context = result.Context
        };

        yield return new PipelineStreamEvent
        {
            Type = result.Success ? PipelineStreamEventType.StepComplete : PipelineStreamEventType.Error,
            StepId = config.StepId,
            Content = result.Output ?? result.ErrorMessage,
            Context = result.Context
        };
    }
}
