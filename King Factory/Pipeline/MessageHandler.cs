using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Tools;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Pipeline;

/// <summary>
/// Types of message output.
/// </summary>
public enum MessageOutputType
{
    Token,
    ToolCall,
    ToolResult,
    Status,
    Error,
    Complete
}

/// <summary>
/// Structured output from message processing.
/// </summary>
public class MessageOutput
{
    public MessageOutputType Type { get; set; }
    public string Content { get; set; } = "";
    public string? ToolName { get; set; }
    public Dictionary<string, object>? ToolArguments { get; set; }
    public bool? ToolSuccess { get; set; }
    public bool IsFinal { get; set; }

    public static MessageOutput Token(string content) => new() { Type = MessageOutputType.Token, Content = content };
    public static MessageOutput ToolCallOutput(string toolName, Dictionary<string, object> args) =>
        new() { Type = MessageOutputType.ToolCall, ToolName = toolName, ToolArguments = args, Content = $"Executing {toolName}..." };
    public static MessageOutput ToolResultOutput(string toolName, bool success, string output) =>
        new() { Type = MessageOutputType.ToolResult, ToolName = toolName, ToolSuccess = success, Content = output };
    public static MessageOutput StatusOutput(string status) => new() { Type = MessageOutputType.Status, Content = status };
    public static MessageOutput ErrorOutput(string error) => new() { Type = MessageOutputType.Error, Content = error };
    public static MessageOutput CompleteOutput() => new() { Type = MessageOutputType.Complete, IsFinal = true };
}

/// <summary>
/// Handles incoming messages and orchestrates the response pipeline.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Process a message and stream the response.
    /// </summary>
    IAsyncEnumerable<string> ProcessAsync(string conversationId, string message, MessageHandlerOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a message with tool execution enabled (legacy string output).
    /// </summary>
    IAsyncEnumerable<string> ProcessWithToolsAsync(string conversationId, string message, MessageHandlerOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a message with tool execution enabled (structured output).
    /// </summary>
    IAsyncEnumerable<MessageOutput> ProcessWithToolsStructuredAsync(string conversationId, string message, MessageHandlerOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get or create a conversation buffer.
    /// </summary>
    IConversationBuffer GetConversation(string conversationId);
}

/// <summary>
/// Main message processing pipeline.
/// </summary>
public class MessageHandler : IMessageHandler
{
    private readonly ILogger<MessageHandler> _logger;
    private readonly ILogger _traceLogger;
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IConversationManager _conversationManager;
    private readonly IToolRouter _toolRouter;
    private readonly int _maxContextTokens;
    private readonly int _maxToolIterations;

    public MessageHandler(
        ILogger<MessageHandler> logger,
        ILoggerFactory loggerFactory,
        IUnifiedLlmProvider llmProvider,
        IPromptBuilder promptBuilder,
        IConversationManager conversationManager,
        IToolRouter toolRouter,
        int maxContextTokens = 4096,
        int maxToolIterations = 10)
    {
        _logger = logger;
        _traceLogger = loggerFactory.CreateLogger("PipelineTrace");
        _llmProvider = llmProvider;
        _promptBuilder = promptBuilder;
        _conversationManager = conversationManager;
        _toolRouter = toolRouter;
        _maxContextTokens = maxContextTokens;
        _maxToolIterations = maxToolIterations;
    }

    public IConversationBuffer GetConversation(string conversationId)
    {
        return _conversationManager.GetOrCreate(conversationId);
    }

    public async IAsyncEnumerable<string> ProcessAsync(
        string conversationId,
        string message,
        MessageHandlerOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new MessageHandlerOptions
        {
            MaxContextTokens = _maxContextTokens,
            MaxToolIterations = _maxToolIterations
        };

        var conversation = _conversationManager.GetOrCreate(conversationId);

        // Add user message
        conversation.AddMessage(new ChatMessage
        {
            Role = "user",
            Content = message
        });

        _logger.LogInformation("Processing message in conversation {ConversationId}", conversationId);
        _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} UserMessage={Message}",
            conversationId, options.PipelineId ?? "unknown", message);

        // Build prompt from conversation
        var messages = GetMessagesForPrompt(conversation, message, options);
        var prompt = _promptBuilder.BuildPrompt(messages, BuildPromptContext(options));

        _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} Prompt={Prompt}",
            conversationId, options.PipelineId ?? "unknown", prompt);

        // Stream response from LLM
        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var token in _llmProvider.StreamAsync(
            prompt,
            options.MaxOutputTokens,
            options.Temperature,
            cancellationToken))
        {
            responseBuilder.Append(token);
            yield return token;
        }

        // Add assistant response to conversation
        conversation.AddMessage(new ChatMessage
        {
            Role = "assistant",
            Content = responseBuilder.ToString()
        });

        _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} Response={Response}",
            conversationId, options.PipelineId ?? "unknown", responseBuilder.ToString());
        _logger.LogInformation("Completed processing for conversation {ConversationId}", conversationId);
    }

    public async IAsyncEnumerable<string> ProcessWithToolsAsync(
        string conversationId,
        string message,
        MessageHandlerOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new MessageHandlerOptions
        {
            MaxContextTokens = _maxContextTokens,
            MaxToolIterations = _maxToolIterations
        };

        var conversation = _conversationManager.GetOrCreate(conversationId);

        // Add user message
        conversation.AddMessage(new ChatMessage
        {
            Role = "user",
            Content = message
        });

        _logger.LogInformation("Processing message with tools in conversation {ConversationId}", conversationId);
        _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} UserMessage={Message}",
            conversationId, options.PipelineId ?? "unknown", message);

        var iteration = 0;

        while (iteration < options.MaxToolIterations)
        {
            iteration++;

            // Build prompt
            var messages = GetMessagesForPrompt(conversation, message, options);
            var prompt = _promptBuilder.BuildPrompt(messages, BuildPromptContext(options));

            _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} Prompt={Prompt}",
                conversationId, options.PipelineId ?? "unknown", prompt);

            // Stream response
            var responseBuilder = new System.Text.StringBuilder();

            await foreach (var token in _llmProvider.StreamAsync(
                prompt,
                options.MaxOutputTokens,
                options.Temperature,
                cancellationToken))
            {
                responseBuilder.Append(token);
                yield return token;
            }

            var response = responseBuilder.ToString();
            _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} Response={Response}",
                conversationId, options.PipelineId ?? "unknown", response);

            // Check for tool calls
            var toolCall = _toolRouter.ParseToolCall(response);

            if (toolCall == null)
            {
                // No tool call, we're done
                conversation.AddMessage(new ChatMessage
                {
                    Role = "assistant",
                    Content = response
                });
                break;
            }

            // Execute tool
            _logger.LogInformation("Executing tool {ToolName} (iteration {Iteration})", toolCall.ToolName, iteration);

            yield return $"\n\n[Executing tool: {toolCall.ToolName}...]\n";

            var toolResult = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);

            // Add assistant message with tool call
            conversation.AddMessage(new ChatMessage
            {
                Role = "assistant",
                Content = response,
                ToolCalls = new List<ToolCall> { toolCall }
            });

            // Add tool result
            conversation.AddMessage(new ChatMessage
            {
                Role = "tool",
                Content = toolResult.Success
                    ? $"Tool '{toolResult.ToolName}' succeeded:\n{toolResult.Output}"
                    : $"Tool '{toolResult.ToolName}' failed:\n{toolResult.Error}",
                ToolCallId = toolCall.Id
            });

            yield return $"\n[Tool result: {(toolResult.Success ? "Success" : "Failed")}]\n\n";

            // Continue loop to let LLM respond to tool result
        }

        if (iteration >= options.MaxToolIterations)
        {
            _logger.LogWarning("Max tool iterations reached for conversation {ConversationId}", conversationId);
            yield return "\n\n[Maximum tool iterations reached. Please try a more specific request.]\n";
        }

        _logger.LogInformation("Completed processing with tools for conversation {ConversationId}", conversationId);
    }

    public async IAsyncEnumerable<MessageOutput> ProcessWithToolsStructuredAsync(
        string conversationId,
        string message,
        MessageHandlerOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new MessageHandlerOptions
        {
            MaxContextTokens = _maxContextTokens,
            MaxToolIterations = _maxToolIterations
        };

        var conversation = _conversationManager.GetOrCreate(conversationId);

        // Add user message
        conversation.AddMessage(new ChatMessage
        {
            Role = "user",
            Content = message
        });

        _logger.LogInformation("Processing message with tools (structured) in conversation {ConversationId}", conversationId);
        _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} UserMessage={Message}",
            conversationId, options.PipelineId ?? "unknown", message);

        yield return MessageOutput.StatusOutput("Analyzing request...");

        var iteration = 0;

        while (iteration < options.MaxToolIterations)
        {
            iteration++;

            // Build prompt
            var messages = GetMessagesForPrompt(conversation, message, options);
            var prompt = _promptBuilder.BuildPrompt(messages, BuildPromptContext(options));

            yield return MessageOutput.StatusOutput("Generating response...");
            _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} Prompt={Prompt}",
                conversationId, options.PipelineId ?? "unknown", prompt);

            // Stream response with tool block detection
            var fullResponse = new System.Text.StringBuilder();
            var visibleText = new System.Text.StringBuilder();
            var toolBlockBuffer = new System.Text.StringBuilder();
            var inToolBlock = false;
            var toolBlockStartIndex = -1;

            await foreach (var token in _llmProvider.StreamAsync(
                prompt,
                options.MaxOutputTokens,
                options.Temperature,
                cancellationToken))
            {
                fullResponse.Append(token);
                var currentText = fullResponse.ToString();

                // Check if we're entering a tool block
                if (!inToolBlock)
                {
                    var toolStart = currentText.LastIndexOf("```tool");
                    if (toolStart >= 0 && toolStart > toolBlockStartIndex)
                    {
                        inToolBlock = true;
                        toolBlockStartIndex = toolStart;

                        // Emit any text before the tool block (if any)
                        var lengthToExtract = toolStart - visibleText.Length;
                        if (lengthToExtract > 0)
                        {
                            var textBefore = currentText.Substring(visibleText.Length, lengthToExtract);
                            if (!string.IsNullOrWhiteSpace(textBefore))
                            {
                                yield return MessageOutput.Token(textBefore);
                                visibleText.Append(textBefore);
                            }
                        }

                        toolBlockBuffer.Clear();
                        toolBlockBuffer.Append(currentText.Substring(toolStart));
                        continue;
                    }

                    // Not in a tool block, stream the token
                    yield return MessageOutput.Token(token);
                    visibleText.Append(token);
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
                        // Tool block complete - parse and execute
                        inToolBlock = false;

                        // Try to parse the tool call from the complete block
                        var toolCall = _toolRouter.ParseToolCall(blockContent);
                        if (toolCall != null)
                        {
                            _logger.LogInformation("Executing tool {ToolName} (iteration {Iteration})", toolCall.ToolName, iteration);
                            yield return MessageOutput.ToolCallOutput(toolCall.ToolName, toolCall.Arguments);

                            // Execute tool
                            var toolResult = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);

                            // Emit tool result event
                            yield return MessageOutput.ToolResultOutput(
                                toolCall.ToolName,
                                toolResult.Success,
                                toolResult.Success ? toolResult.Output : (toolResult.Error ?? "Unknown error")
                            );

                            // Show success message to user
                            var resultMsg = toolResult.Success
                                ? $"\n✅ {toolResult.Output}\n"
                                : $"\n❌ {toolResult.Error}\n";
                            yield return MessageOutput.Token(resultMsg);
                            visibleText.Append(resultMsg);
                        }

                        // Check for text after the tool block
                        var afterBlock = blockContent.Substring(endIndex + 3);
                        if (!string.IsNullOrWhiteSpace(afterBlock))
                        {
                            yield return MessageOutput.Token(afterBlock);
                            visibleText.Append(afterBlock);
                        }

                        toolBlockBuffer.Clear();
                    }
                }
            }

            var response = fullResponse.ToString();
            _traceLogger.LogInformation("Conversation={ConversationId} Pipeline={PipelineId} Response={Response}",
                conversationId, options.PipelineId ?? "unknown", response);

            // Check for any remaining tool calls that weren't executed during streaming
            var finalToolCall = _toolRouter.ParseToolCall(response);
            var hasUnexecutedTool = finalToolCall != null && !visibleText.ToString().Contains("✅") && !visibleText.ToString().Contains("❌");

            if (hasUnexecutedTool && finalToolCall != null)
            {
                // Execute any tool that wasn't caught during streaming
                _logger.LogInformation("Executing remaining tool {ToolName} (iteration {Iteration})", finalToolCall.ToolName, iteration);
                yield return MessageOutput.ToolCallOutput(finalToolCall.ToolName, finalToolCall.Arguments);

                var toolResult = await _toolRouter.ExecuteAsync(finalToolCall, cancellationToken);
                yield return MessageOutput.ToolResultOutput(
                    finalToolCall.ToolName,
                    toolResult.Success,
                    toolResult.Success ? toolResult.Output : (toolResult.Error ?? "Unknown error")
                );

                // Add to conversation
                conversation.AddMessage(new ChatMessage
                {
                    Role = "assistant",
                    Content = response,
                    ToolCalls = new List<ToolCall> { finalToolCall }
                });

                conversation.AddMessage(new ChatMessage
                {
                    Role = "tool",
                    Content = toolResult.Success
                        ? $"Tool '{toolResult.ToolName}' succeeded:\n{toolResult.Output}"
                        : $"Tool '{toolResult.ToolName}' failed:\n{toolResult.Error}",
                    ToolCallId = finalToolCall.Id
                });

                // Continue loop for LLM to respond to result
                continue;
            }

            // No tool call found, we're done
            conversation.AddMessage(new ChatMessage
            {
                Role = "assistant",
                Content = response
            });
            yield return MessageOutput.CompleteOutput();
            break;
        }

        if (iteration >= options.MaxToolIterations)
        {
            _logger.LogWarning("Max tool iterations reached for conversation {ConversationId}", conversationId);
            yield return MessageOutput.ErrorOutput("Maximum tool iterations reached. Please try a more specific request.");
        }

        _logger.LogInformation("Completed processing with tools (structured) for conversation {ConversationId}", conversationId);
    }

    private static IReadOnlyList<ChatMessage> GetMessagesForPrompt(
        IConversationBuffer conversation,
        string message,
        MessageHandlerOptions options)
    {
        if (options.ForceLastUser || !options.InjectConversation)
        {
            var lastUser = conversation.GetMessages().LastOrDefault(m => m.Role == "user");
            return new List<ChatMessage>
            {
                lastUser ?? new ChatMessage { Role = "user", Content = message }
            };
        }

        return conversation.GetWindowedMessages(options.MaxContextTokens);
    }

    private static PromptContext BuildPromptContext(MessageHandlerOptions options)
    {
        return new PromptContext
        {
            PlanningMode = options.PlanningMode,
            ToolsContext = options.EnableTools ? "enabled" : null,
            TaskLock = options.TaskLock,
            CodeMode = options.CodeMode,
            FixMode = options.FixMode,
            DeveloperPrompt = options.DeveloperPrompt,
            SystemPromptOverride = options.SystemPromptOverride,
            SuppressToolDescriptions = options.SuppressToolDescriptions
        };
    }
}

/// <summary>
/// Factory for creating message handlers with specific configurations.
/// </summary>
public interface IMessageHandlerFactory
{
    IMessageHandler Create(MessageHandlerOptions? options = null);
}

/// <summary>
/// Options for configuring message handlers.
/// </summary>
public class MessageHandlerOptions
{
    public int MaxContextTokens { get; set; } = 4096;
    public int MaxToolIterations { get; set; } = 10;
    public bool EnableTools { get; set; } = true;
    public bool InjectConversation { get; set; } = true;
    public bool PlanningMode { get; set; }
    public int? MaxOutputTokens { get; set; }
    public float? Temperature { get; set; }
    public string? PipelineId { get; set; }
    public string? PipelineName { get; set; }
    public string? TaskLock { get; set; }
    public bool ForceLastUser { get; set; }
    public bool CodeMode { get; set; }
    public bool FixMode { get; set; }
    public string? DeveloperPrompt { get; set; }
    public string? SystemPromptOverride { get; set; }
    public bool SuppressToolDescriptions { get; set; }
}

/// <summary>
/// Message handler factory implementation.
/// </summary>
public class MessageHandlerFactory : IMessageHandlerFactory
{
    private readonly ILogger<MessageHandler> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IConversationManager _conversationManager;
    private readonly IToolRouter _toolRouter;

    public MessageHandlerFactory(
        ILogger<MessageHandler> logger,
        ILoggerFactory loggerFactory,
        IUnifiedLlmProvider llmProvider,
        IPromptBuilder promptBuilder,
        IConversationManager conversationManager,
        IToolRouter toolRouter)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _llmProvider = llmProvider;
        _promptBuilder = promptBuilder;
        _conversationManager = conversationManager;
        _toolRouter = toolRouter;
    }

    public IMessageHandler Create(MessageHandlerOptions? options = null)
    {
        options ??= new MessageHandlerOptions();

        return new MessageHandler(
            _logger,
            _loggerFactory,
            _llmProvider,
            _promptBuilder,
            _conversationManager,
            _toolRouter,
            options.MaxContextTokens,
            options.MaxToolIterations
        );
    }
}
