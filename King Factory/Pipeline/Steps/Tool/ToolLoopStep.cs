using System.Text;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Tools;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Tool;

/// <summary>
/// Executes a tool loop: parse, execute, respond, repeat until no more tools.
/// </summary>
public sealed class ToolLoopStep : PipelineStepBase
{
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly Pipeline.IPromptBuilder _promptBuilder;
    private readonly IToolRouter _toolRouter;

    public override string TypeId => "tool.loop";
    public override string DisplayName => "Tool Loop";
    public override string Category => "Tool";
    public override string Description => "Executes a tool loop that parses tool calls, executes them, and continues until no more tools are called.";
    public override bool SupportsStreaming => true;

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "maxIterations",
            DisplayName = "Max Iterations",
            Type = StepParameterType.Integer,
            Description = "Maximum number of tool loop iterations",
            DefaultValue = 10,
            MinValue = 1,
            MaxValue = 50
        },
        new StepParameterDefinition
        {
            Name = "temperature",
            DisplayName = "Temperature",
            Type = StepParameterType.Float,
            Description = "Temperature for LLM responses",
            DefaultValue = 0.7f
        },
        new StepParameterDefinition
        {
            Name = "maxTokens",
            DisplayName = "Max Tokens",
            Type = StepParameterType.Integer,
            Description = "Maximum tokens per response",
            DefaultValue = 2048
        }
    );

    public ToolLoopStep(
        IUnifiedLlmProvider llmProvider,
        Pipeline.IPromptBuilder promptBuilder,
        IToolRouter toolRouter)
    {
        _llmProvider = llmProvider;
        _promptBuilder = promptBuilder;
        _toolRouter = toolRouter;
    }

    public override async Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var finalContext = context;
        var responseBuilder = new StringBuilder();

        await foreach (var evt in ExecuteStreamingAsync(context, config, cancellationToken))
        {
            if (evt.Context != null)
            {
                finalContext = evt.Context;
            }

            if (evt.Type == PipelineStreamEventType.Token && evt.Content != null)
            {
                responseBuilder.Append(evt.Content);
            }
        }

        return Success(finalContext, responseBuilder.ToString());
    }

    public override async IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        PipelineContext context,
        StepConfiguration config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxIterations = GetParameter<int>(config, "maxIterations", 10);
        var temperature = GetParameter<float>(config, "temperature", context.LlmParameters.Temperature);
        var maxTokens = GetParameter<int>(config, "maxTokens", context.LlmParameters.MaxOutputTokens);

        var currentContext = context;
        var iteration = 0;

        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.StepStarted,
            StepId = config.StepId,
            Context = currentContext
        };

        while (iteration < maxIterations)
        {
            iteration++;

            // Build prompt
            var prompt = _promptBuilder.BuildPrompt(
                currentContext.Messages.ToList(),
                new Pipeline.PromptContext
                {
                    SystemPromptOverride = currentContext.SystemPrompt,
                    ToolsContext = currentContext.Tools.Count > 0 ? "enabled" : null
                });

            // Generate response
            var responseBuilder = new StringBuilder();
            await foreach (var token in _llmProvider.StreamAsync(prompt, maxTokens, temperature, cancellationToken))
            {
                responseBuilder.Append(token);
                yield return new PipelineStreamEvent
                {
                    Type = PipelineStreamEventType.Token,
                    StepId = config.StepId,
                    Content = token,
                    Context = currentContext
                };
            }

            var response = responseBuilder.ToString();

            // Parse tool call
            var toolCall = _toolRouter.ParseToolCall(response);

            if (toolCall == null)
            {
                // No more tools - we're done
                currentContext = currentContext
                    .WithMessage(new ChatMessage { Role = "assistant", Content = response })
                    .WithNewResponseText(response);
                break;
            }

            // Execute tool
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.ToolCall,
                StepId = config.StepId,
                ToolName = toolCall.ToolName,
                ToolArguments = toolCall.Arguments,
                Context = currentContext
            };

            var toolResult = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);

            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.ToolResult,
                StepId = config.StepId,
                ToolName = toolCall.ToolName,
                ToolSuccess = toolResult.Success,
                Content = toolResult.Success ? toolResult.Output : toolResult.Error,
                Context = currentContext
            };

            // Update context with tool result
            currentContext = currentContext
                .WithToolResult(new ToolExecutionResult
                {
                    ToolName = toolCall.ToolName,
                    ToolCallId = toolCall.Id,
                    Success = toolResult.Success,
                    Output = toolResult.Output,
                    Error = toolResult.Error,
                    Arguments = toolCall.Arguments
                })
                .WithMessage(new ChatMessage
                {
                    Role = "assistant",
                    Content = response,
                    ToolCalls = new List<ToolCall> { toolCall }
                })
                .WithMessage(new ChatMessage
                {
                    Role = "tool",
                    Content = toolResult.Success
                        ? $"Tool '{toolCall.ToolName}' succeeded:\n{toolResult.Output}"
                        : $"Tool '{toolCall.ToolName}' failed:\n{toolResult.Error}",
                    ToolCallId = toolCall.Id
                });

            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Progress,
                StepId = config.StepId,
                Content = $"Tool loop iteration {iteration}/{maxIterations}",
                Context = currentContext
            };
        }

        if (iteration >= maxIterations)
        {
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Warning,
                StepId = config.StepId,
                Content = "Maximum tool iterations reached",
                Context = currentContext
            };
        }

        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.StepComplete,
            StepId = config.StepId,
            Content = $"Tool loop completed after {iteration} iterations",
            Context = currentContext
        };
    }
}
