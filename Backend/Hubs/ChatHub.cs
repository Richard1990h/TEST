using Microsoft.AspNetCore.SignalR;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Backend.Helpers;
using System.Text;
using System.Collections.Concurrent;
using LittleHelperAI.KingFactory;

namespace LittleHelperAI.Backend.Hubs;

/// <summary>
/// SignalR Hub for real-time chat communication.
/// Uses the Factory system for AI processing.
/// </summary>
public class ChatHub : Hub
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatHub> _logger;
    private readonly IFactory _factory;

    // Track active generations for cancellation
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _activeGenerations = new();

    public ChatHub(
        IServiceProvider serviceProvider,
        ILogger<ChatHub> logger,
        IFactory factory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _factory = factory;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("[ChatHub] Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("[ChatHub] Client disconnected: {ConnectionId}, Error: {Error}",
            Context.ConnectionId, exception?.Message ?? "None");

        // Cancel any active generation for this connection
        if (_activeGenerations.TryRemove(Context.ConnectionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Receive a chat message, process with Factory, and stream response.
    /// </summary>
    public async Task SendMessage(ChatRequest request)
    {
        _logger.LogInformation("[ChatHub] SendMessage called for user {UserId}", request.UserId);

        var cts = new CancellationTokenSource();
        _activeGenerations[Context.ConnectionId] = cts;

        try
        {
            if ((string.IsNullOrWhiteSpace(request.Message) && (request.Files == null || !request.Files.Any()))
                || request.UserId <= 0)
            {
                await Clients.Caller.SendAsync("ReceiveError", "Invalid request.");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId);
            if (user == null)
            {
                await Clients.Caller.SendAsync("ReceiveError", "Unauthorized");
                return;
            }

            var rawMessage = (request.Message ?? "").Trim();
            var hasFiles = request.Files != null && request.Files.Any();

            ChatHistory chat;
            if (request.CreateNewChat)
            {
                chat = new ChatHistory
                {
                    UserId = user.Id,
                    Message = "",
                    Reply = "",
                    Title = rawMessage.Length > 50 ? rawMessage[..47] + "..." : rawMessage,
                    Timestamp = DateTime.UtcNow
                };
                context.ChatHistory.Add(chat);
                await context.SaveChangesAsync();
                _logger.LogInformation("[ChatHub] Created new chat {ChatId} for user {UserId}", chat.Id, user.Id);
            }
            else
            {
                if (!request.ChatId.HasValue || request.ChatId.Value <= 0)
                {
                    await Clients.Caller.SendAsync("ReceiveError", "ChatId is required");
                    return;
                }

                var foundChat = await context.ChatHistory
                    .FirstOrDefaultAsync(c => c.Id == request.ChatId.Value && c.UserId == user.Id);

                if (foundChat == null)
                {
                    await Clients.Caller.SendAsync("ReceiveError", "Invalid ChatId");
                    return;
                }
                chat = foundChat;
            }

            await Clients.Caller.SendAsync("ReceiveChatId", chat.Id);

            // Pipeline step: Inject context
            await Clients.Caller.SendAsync("ReceivePipelineStep", "inject");

            var transcript = LoadTranscript(chat);
            transcript.Turns.Add(new ChatTurn
            {
                Role = "user",
                Text = rawMessage,
                Utc = DateTime.UtcNow
            });

            string finalMessage = rawMessage;
            if (hasFiles)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Files attached:");
                foreach (var file in request.Files!)
                {
                    sb.AppendLine($"- {file.Name}");
                }
                sb.AppendLine("\nMessage: " + rawMessage);
                finalMessage = sb.ToString();
            }

            // Store the user message
            chat.Message = finalMessage;
            chat.Timestamp = DateTime.UtcNow;
            SaveTranscript(chat, transcript);
            await context.SaveChangesAsync();

            // Check if Factory is ready
            if (!_factory.IsReady)
            {
                try
                {
                    await _factory.InitializeAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ChatHub] Factory initialization failed - model may not be configured");
                    await Clients.Caller.SendAsync("ReceiveToken", "[AI model not configured. Please set Factory:ModelPath in configuration.]");
                    await Clients.Caller.SendAsync("ReceiveComplete", new
                    {
                        chatId = chat.Id,
                        used = 0,
                        creditsLeft = 0,
                        tokens = 0
                    });
                    return;
                }
            }

            // Pipeline step: Analyze intent
            await Clients.Caller.SendAsync("ReceivePipelineStep", "analyze");

            // Process with Factory and stream response
            var responseBuilder = new StringBuilder();
            var tokenCount = 0;
            var conversationId = $"chat_{chat.Id}";
            var hasStartedGeneration = false;
            var hasUsedTools = false;

            try
            {
                await foreach (var output in _factory.ProcessInConversationAsync(
                    conversationId,
                    finalMessage,
                    new FactoryOptions { EnableTools = true, EnableReasoning = true },
                    cts.Token))
                {
                    switch (output.Type)
                    {
                        case FactoryOutputType.Token:
                            // Pipeline step: Generate (on first token)
                            if (!hasStartedGeneration)
                            {
                                hasStartedGeneration = true;
                                await Clients.Caller.SendAsync("ReceivePipelineStep", "generate");
                            }
                            responseBuilder.Append(output.Content);
                            tokenCount++;
                            await Clients.Caller.SendAsync("ReceiveToken", output.Content);
                            break;

                        case FactoryOutputType.Thinking:
                        case FactoryOutputType.Planning:
                            await Clients.Caller.SendAsync("ReceiveStatus", output.Content);
                            break;

                        case FactoryOutputType.ToolCall:
                            // Pipeline step: Tools (on first tool call)
                            if (!hasUsedTools)
                            {
                                hasUsedTools = true;
                                await Clients.Caller.SendAsync("ReceivePipelineStep", "tools");
                            }
                            // Send detailed tool call info
                            var toolCallInfo = new
                            {
                                toolName = output.Metadata?.GetValueOrDefault("toolName") ?? "unknown",
                                arguments = output.Metadata?.GetValueOrDefault("toolArguments"),
                                message = output.Content
                            };
                            await Clients.Caller.SendAsync("ReceiveToolCall", JsonSerializer.Serialize(toolCallInfo));
                            await Clients.Caller.SendAsync("ReceiveStatus", $"Executing tool: {toolCallInfo.toolName}...");
                            break;

                        case FactoryOutputType.ToolResult:
                            // Send detailed tool result info
                            var toolResultInfo = new
                            {
                                toolName = output.Metadata?.GetValueOrDefault("toolName") ?? "unknown",
                                success = output.Metadata?.GetValueOrDefault("toolSuccess") ?? false,
                                output = output.Content
                            };
                            await Clients.Caller.SendAsync("ReceiveToolResult", JsonSerializer.Serialize(toolResultInfo));

                            // Show result in chat if it's visible feedback
                            if (output.Content.Contains("Created:") || output.Content.Contains("Updated:"))
                            {
                                await Clients.Caller.SendAsync("ReceiveToken", $"\n✅ {output.Content}\n");
                                responseBuilder.AppendLine($"\n✅ {output.Content}");
                            }
                            break;

                        case FactoryOutputType.Progress:
                            // Send as status update
                            await Clients.Caller.SendAsync("ReceiveStatus", output.Content);
                            if (output.Metadata != null)
                            {
                                await Clients.Caller.SendAsync("ReceiveProgress", new
                                {
                                    step = output.Metadata.GetValueOrDefault("step"),
                                    progress = output.Metadata.GetValueOrDefault("progress")
                                });
                            }
                            break;

                        case FactoryOutputType.Error:
                            _logger.LogWarning("[ChatHub] Factory error: {Error}", output.Content);
                            await Clients.Caller.SendAsync("ReceiveToken", $"\n\n⚠️ {output.Content}");
                            responseBuilder.AppendLine($"\n\n⚠️ {output.Content}");
                            break;

                        case FactoryOutputType.Complete:
                            // Final output handling
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[ChatHub] Generation cancelled for chat {ChatId}", chat.Id);
                await Clients.Caller.SendAsync("ReceiveToken", "\n\n[Generation stopped]");
                responseBuilder.AppendLine("\n\n[Generation stopped]");
            }

            // Store the assistant response
            var fullResponse = responseBuilder.ToString();
            transcript.Turns.Add(new ChatTurn
            {
                Role = "assistant",
                Text = fullResponse,
                Utc = DateTime.UtcNow
            });

            chat.Reply = fullResponse;
            SaveTranscript(chat, transcript);
            await context.SaveChangesAsync();

            // Pipeline step: Validate (completion step)
            await Clients.Caller.SendAsync("ReceivePipelineStep", "validate");

            await Clients.Caller.SendAsync("ReceiveComplete", new
            {
                chatId = chat.Id,
                used = 0, // Credit tracking could be added
                creditsLeft = 0,
                tokens = tokenCount
            });

            _logger.LogInformation("[ChatHub] Completed processing for chat {ChatId}, {Tokens} tokens", chat.Id, tokenCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatHub] Error in SendMessage");
            await Clients.Caller.SendAsync("ReceiveError", $"Error: {ex.Message}");
        }
        finally
        {
            _activeGenerations.TryRemove(Context.ConnectionId, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Stop the current generation.
    /// </summary>
    public Task StopGeneration()
    {
        _logger.LogInformation("[ChatHub] StopGeneration called for connection {ConnectionId}", Context.ConnectionId);

        if (_activeGenerations.TryGetValue(Context.ConnectionId, out var cts))
        {
            cts.Cancel();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Report terminal/preview errors for the AI to fix.
    /// </summary>
    public async Task ReportError(ErrorReport report)
    {
        _logger.LogInformation("[ChatHub] Error reported: {ErrorType} - {Message}", report.ErrorType, report.Message);

        if (report.ChatId <= 0)
        {
            await Clients.Caller.SendAsync("ReceiveError", "Invalid chat ID for error report");
            return;
        }

        // Format the error message for the AI
        var errorMessage = $@"[ERROR DETECTED] The following error occurred in the preview:

Error Type: {report.ErrorType}
Error Message: {report.Message}
{(string.IsNullOrEmpty(report.StackTrace) ? "" : $"\nStack Trace:\n{report.StackTrace}")}
{(string.IsNullOrEmpty(report.FilePath) ? "" : $"\nFile: {report.FilePath}")}
{(report.LineNumber > 0 ? $"\nLine: {report.LineNumber}" : "")}

Please fix this error and update the affected file(s).";

        // Create a new chat request with the error
        var request = new ChatRequest
        {
            UserId = report.UserId,
            ChatId = report.ChatId,
            Message = errorMessage,
            CreateNewChat = false
        };

        // Process through the normal message flow
        await SendMessage(request);
    }

    public class ErrorReport
    {
        public int UserId { get; set; }
        public int ChatId { get; set; }
        public string ErrorType { get; set; } = "Runtime";
        public string Message { get; set; } = "";
        public string? StackTrace { get; set; }
        public string? FilePath { get; set; }
        public int LineNumber { get; set; }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static ChatTranscript LoadTranscript(ChatHistory chat)
    {
        if (string.IsNullOrWhiteSpace(chat.Metadata))
            return new ChatTranscript();

        try
        {
            var transcript = JsonSerializer.Deserialize<ChatTranscript>(chat.Metadata, _jsonOpts);
            if (transcript?.Turns != null)
                return transcript;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChatHub] GetTranscript deserialization failed for chat {chat.Id}: {ex.Message}");
        }

        return new ChatTranscript();
    }

    private static void SaveTranscript(ChatHistory chat, ChatTranscript transcript)
    {
        chat.Metadata = JsonSerializer.Serialize(transcript, _jsonOpts);
    }
}
