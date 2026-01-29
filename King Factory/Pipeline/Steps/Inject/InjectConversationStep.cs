using LittleHelperAI.KingFactory.Context;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Inject;

/// <summary>
/// Injects conversation history into the pipeline context.
/// </summary>
public sealed class InjectConversationStep : PipelineStepBase
{
    private readonly IConversationManager _conversationManager;

    public override string TypeId => "inject.conversation";
    public override string DisplayName => "Inject Conversation";
    public override string Category => "Inject";
    public override string Description => "Injects conversation history and the current user message into the context.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "maxMessages",
            DisplayName = "Max Messages",
            Type = StepParameterType.Integer,
            Description = "Maximum number of messages to include from history",
            DefaultValue = 20,
            MinValue = 1,
            MaxValue = 100
        },
        new StepParameterDefinition
        {
            Name = "maxTokens",
            DisplayName = "Max Tokens",
            Type = StepParameterType.Integer,
            Description = "Maximum tokens to use for conversation history",
            DefaultValue = 2048,
            MinValue = 256,
            MaxValue = 32768
        },
        new StepParameterDefinition
        {
            Name = "includeHistory",
            DisplayName = "Include History",
            Type = StepParameterType.Boolean,
            Description = "Whether to include previous conversation history",
            DefaultValue = true
        },
        new StepParameterDefinition
        {
            Name = "userMessageOnly",
            DisplayName = "User Message Only",
            Type = StepParameterType.Boolean,
            Description = "Only include the current user message, not history",
            DefaultValue = false
        }
    );

    public InjectConversationStep(IConversationManager conversationManager)
    {
        _conversationManager = conversationManager;
    }

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var userMessageOnly = GetParameter<bool>(config, "userMessageOnly", false);
        var includeHistory = GetParameter<bool>(config, "includeHistory", true);
        var maxMessages = GetParameter<int>(config, "maxMessages", 20);
        var maxTokens = GetParameter<int>(config, "maxTokens", 2048);

        var messages = new List<ChatMessage>();

        // Add conversation history if requested
        if (includeHistory && !userMessageOnly && context.Input.ConversationHistory != null)
        {
            var history = context.Input.ConversationHistory.TakeLast(maxMessages).ToList();
            messages.AddRange(history);
        }

        // Also check for existing conversation in manager
        if (includeHistory && !userMessageOnly)
        {
            var conversation = _conversationManager.GetOrCreate(context.ConversationId);
            var existingMessages = conversation.GetWindowedMessages(maxTokens);

            // Don't duplicate messages
            var existingContents = new HashSet<string>(messages.Select(m => m.Content));
            foreach (var msg in existingMessages)
            {
                if (!existingContents.Contains(msg.Content))
                {
                    messages.Add(msg);
                }
            }
        }

        // Always add the current user message
        var userMessage = new ChatMessage
        {
            Role = "user",
            Content = context.Input.Message
        };

        // Check if we already have this message
        if (!messages.Any(m => m.Role == "user" && m.Content == context.Input.Message))
        {
            messages.Add(userMessage);
        }

        // Add messages to context
        var newContext = context.WithMessages(messages);

        // Track the conversation
        var conversation2 = _conversationManager.GetOrCreate(context.ConversationId);
        conversation2.AddMessage(userMessage);

        return Task.FromResult(Success(newContext, $"Injected {messages.Count} messages"));
    }
}
