using System.Text;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Tools;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.LLM;

/// <summary>
/// Streams LLM response with tool detection, parsing, and execution.
/// This is the primary step for tool-enabled conversations.
/// </summary>
public sealed class LlmStreamWithToolsStep : PipelineStepBase
{
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly Pipeline.IPromptBuilder _promptBuilder;
    private readonly IToolRouter _toolRouter;

    public override string TypeId => "llm.stream-with-tools";
    public override string DisplayName => "LLM Stream with Tools";
    public override string Category => "LLM";
    public override string Description => "Generates a streaming response with automatic tool detection, parsing, and execution. Supports multi-turn tool loops.";
    public override bool SupportsStreaming => true;

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "temperature",
            DisplayName = "Temperature",
            Type = StepParameterType.Float,
            Description = "Sampling temperature (0.0 - 2.0)",
            DefaultValue = 0.7f,
            MinValue = 0.0f,
            MaxValue = 2.0f
        },
        new StepParameterDefinition
        {
            Name = "maxTokens",
            DisplayName = "Max Tokens",
            Type = StepParameterType.Integer,
            Description = "Maximum tokens to generate per turn",
            DefaultValue = 2048
        },
        new StepParameterDefinition
        {
            Name = "maxToolIterations",
            DisplayName = "Max Tool Iterations",
            Type = StepParameterType.Integer,
            Description = "Maximum number of tool execution loops",
            DefaultValue = 10,
            MinValue = 1,
            MaxValue = 50
        },
        new StepParameterDefinition
        {
            Name = "autoExecuteTools",
            DisplayName = "Auto Execute Tools",
            Type = StepParameterType.Boolean,
            Description = "Automatically execute detected tool calls",
            DefaultValue = true
        }
    );

    public LlmStreamWithToolsStep(
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
        // For non-streaming, collect all events
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
        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.StepStarted,
            StepId = config.StepId,
            Context = context
        };

        var temperature = GetParameter<float>(config, "temperature", context.LlmParameters.Temperature);
        var maxTokens = GetParameter<int>(config, "maxTokens", context.LlmParameters.MaxOutputTokens);
        var maxToolIterations = GetParameter<int>(config, "maxToolIterations", context.Pipeline.Config.MaxToolIterations);
        var autoExecuteTools = GetParameter<bool>(config, "autoExecuteTools", true);

        var currentContext = context;
        var iteration = 0;
        var allResponses = new StringBuilder();

        while (iteration < maxToolIterations)
        {
            iteration++;

            // Build prompt with current context
            var prompt = BuildPrompt(currentContext);

            // Stream response with tool detection
            var responseBuilder = new StringBuilder();
            var toolBlockBuffer = new StringBuilder();
            var inToolBlock = false;
            var toolBlockStart = -1;
            var executedToolThisIteration = false;

            await foreach (var token in _llmProvider.StreamAsync(prompt, maxTokens, temperature, cancellationToken))
            {
                responseBuilder.Append(token);
                var currentText = responseBuilder.ToString();

                // Check for tool block start
                if (!inToolBlock)
                {
                    var toolStart = currentText.LastIndexOf("```tool");
                    if (toolStart >= 0 && toolStart > toolBlockStart)
                    {
                        inToolBlock = true;
                        toolBlockStart = toolStart;
                        toolBlockBuffer.Clear();
                        toolBlockBuffer.Append(currentText.Substring(toolStart));

                        // Emit any text before the tool block
                        var textBefore = currentText.Substring(0, toolStart);
                        if (!string.IsNullOrWhiteSpace(textBefore) && textBefore.Length > allResponses.Length)
                        {
                            var newText = textBefore.Substring(allResponses.Length);
                            if (!string.IsNullOrEmpty(newText))
                            {
                                yield return new PipelineStreamEvent
                                {
                                    Type = PipelineStreamEventType.Token,
                                    StepId = config.StepId,
                                    Content = newText,
                                    Context = currentContext
                                };
                                allResponses.Append(newText);
                            }
                        }
                        continue;
                    }

                    // Not in tool block, stream the token
                    yield return new PipelineStreamEvent
                    {
                        Type = PipelineStreamEventType.Token,
                        StepId = config.StepId,
                        Content = token,
                        Context = currentContext
                    };
                    allResponses.Append(token);
                }
                else
                {
                    // In tool block - buffer but don't stream
                    toolBlockBuffer.Append(token);

                    // Check if tool block is complete
                    var blockContent = toolBlockBuffer.ToString();
                    var endIndex = blockContent.IndexOf("```", 7); // After initial ```tool
                    if (endIndex >= 0)
                    {
                        // Tool block complete
                        inToolBlock = false;

                        if (autoExecuteTools)
                        {
                            // Parse and execute tool
                            var toolCall = _toolRouter.ParseToolCall(blockContent);
                            if (toolCall != null)
                            {
                                executedToolThisIteration = true;

                                // Emit tool call event
                                yield return new PipelineStreamEvent
                                {
                                    Type = PipelineStreamEventType.ToolCall,
                                    StepId = config.StepId,
                                    ToolName = toolCall.ToolName,
                                    ToolArguments = toolCall.Arguments,
                                    Context = currentContext
                                };

                                // Execute tool
                                var toolResult = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);

                                // Emit tool result event
                                yield return new PipelineStreamEvent
                                {
                                    Type = PipelineStreamEventType.ToolResult,
                                    StepId = config.StepId,
                                    ToolName = toolCall.ToolName,
                                    ToolSuccess = toolResult.Success,
                                    Content = toolResult.Success ? toolResult.Output : toolResult.Error,
                                    Context = currentContext
                                };

                                // Add tool result to context
                                currentContext = currentContext.WithToolResult(new ToolExecutionResult
                                {
                                    ToolName = toolCall.ToolName,
                                    ToolCallId = toolCall.Id,
                                    Success = toolResult.Success,
                                    Output = toolResult.Output,
                                    Error = toolResult.Error,
                                    Arguments = toolCall.Arguments
                                });

                                // Add messages to conversation
                                currentContext = currentContext
                                    .WithMessage(new ChatMessage
                                    {
                                        Role = "assistant",
                                        Content = responseBuilder.ToString(),
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

                                // Emit result message to user
                                var resultMsg = toolResult.Success
                                    ? $"\n[Tool {toolCall.ToolName}: Success]\n"
                                    : $"\n[Tool {toolCall.ToolName}: Failed - {toolResult.Error}]\n";

                                yield return new PipelineStreamEvent
                                {
                                    Type = PipelineStreamEventType.Token,
                                    StepId = config.StepId,
                                    Content = resultMsg,
                                    Context = currentContext
                                };
                                allResponses.Append(resultMsg);
                            }
                        }

                        // Check for text after the tool block
                        var afterBlock = blockContent.Substring(endIndex + 3);
                        if (!string.IsNullOrWhiteSpace(afterBlock))
                        {
                            yield return new PipelineStreamEvent
                            {
                                Type = PipelineStreamEventType.Token,
                                StepId = config.StepId,
                                Content = afterBlock,
                                Context = currentContext
                            };
                            allResponses.Append(afterBlock);
                        }

                        toolBlockBuffer.Clear();
                    }
                }
            }

            // Check for any remaining tool call not caught during streaming
            if (!executedToolThisIteration && autoExecuteTools)
            {
                var finalToolCall = _toolRouter.ParseToolCall(responseBuilder.ToString());
                if (finalToolCall != null)
                {
                    executedToolThisIteration = true;

                    yield return new PipelineStreamEvent
                    {
                        Type = PipelineStreamEventType.ToolCall,
                        StepId = config.StepId,
                        ToolName = finalToolCall.ToolName,
                        ToolArguments = finalToolCall.Arguments,
                        Context = currentContext
                    };

                    var toolResult = await _toolRouter.ExecuteAsync(finalToolCall, cancellationToken);

                    yield return new PipelineStreamEvent
                    {
                        Type = PipelineStreamEventType.ToolResult,
                        StepId = config.StepId,
                        ToolName = finalToolCall.ToolName,
                        ToolSuccess = toolResult.Success,
                        Content = toolResult.Success ? toolResult.Output : toolResult.Error,
                        Context = currentContext
                    };

                    currentContext = currentContext
                        .WithToolResult(new ToolExecutionResult
                        {
                            ToolName = finalToolCall.ToolName,
                            ToolCallId = finalToolCall.Id,
                            Success = toolResult.Success,
                            Output = toolResult.Output,
                            Error = toolResult.Error,
                            Arguments = finalToolCall.Arguments
                        })
                        .WithMessage(new ChatMessage
                        {
                            Role = "assistant",
                            Content = responseBuilder.ToString(),
                            ToolCalls = new List<ToolCall> { finalToolCall }
                        })
                        .WithMessage(new ChatMessage
                        {
                            Role = "tool",
                            Content = toolResult.Success
                                ? $"Tool '{finalToolCall.ToolName}' succeeded:\n{toolResult.Output}"
                                : $"Tool '{finalToolCall.ToolName}' failed:\n{toolResult.Error}",
                            ToolCallId = finalToolCall.Id
                        });
                }
            }

            // If no tool was executed, we're done
            if (!executedToolThisIteration)
            {
                // Add final assistant message
                currentContext = currentContext.WithMessage(new ChatMessage
                {
                    Role = "assistant",
                    Content = responseBuilder.ToString()
                });
                break;
            }

            // Continue loop for LLM to respond to tool result
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Progress,
                StepId = config.StepId,
                Content = $"Tool iteration {iteration}/{maxToolIterations}",
                Context = currentContext
            };
        }

        if (iteration >= maxToolIterations)
        {
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Warning,
                StepId = config.StepId,
                Content = "Maximum tool iterations reached",
                Context = currentContext
            };
        }

        // Update final response
        currentContext = currentContext.WithNewResponseText(allResponses.ToString());

        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.StepComplete,
            StepId = config.StepId,
            Context = currentContext
        };
    }

    private string BuildPrompt(PipelineContext context)
    {
        return _promptBuilder.BuildPrompt(
            context.Messages.ToList(),
            new Pipeline.PromptContext
            {
                SystemPromptOverride = context.SystemPrompt,
                ToolsContext = context.Tools.Count > 0 ? "enabled" : null
            });
    }
}
