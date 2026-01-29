using System.Collections.Concurrent;
using System.Collections.Immutable;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Tools;

namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// Immutable, thread-safe context passed through pipeline steps.
/// Each step receives the context and can create a modified copy for subsequent steps.
/// </summary>
public sealed class PipelineContext
{
    /// <summary>
    /// Unique identifier for this pipeline execution.
    /// </summary>
    public string ExecutionId { get; }

    /// <summary>
    /// The pipeline definition being executed.
    /// </summary>
    public PipelineDefinitionV2 Pipeline { get; }

    /// <summary>
    /// The original user input that triggered this pipeline.
    /// </summary>
    public PipelineInput Input { get; }

    /// <summary>
    /// Conversation ID for this execution.
    /// </summary>
    public string ConversationId { get; }

    /// <summary>
    /// User ID if authenticated.
    /// </summary>
    public int? UserId { get; }

    /// <summary>
    /// When the execution started.
    /// </summary>
    public DateTime StartedAt { get; }

    /// <summary>
    /// Accumulated messages for the LLM prompt.
    /// </summary>
    public ImmutableList<ChatMessage> Messages { get; }

    /// <summary>
    /// System prompt to use.
    /// </summary>
    public string? SystemPrompt { get; }

    /// <summary>
    /// Available tools for this execution.
    /// </summary>
    public ImmutableList<ITool> Tools { get; }

    /// <summary>
    /// Tool results from executed tools.
    /// </summary>
    public ImmutableList<ToolExecutionResult> ToolResults { get; }

    /// <summary>
    /// Variables set by steps (e.g., extracted requirements, classifications).
    /// </summary>
    public ImmutableDictionary<string, object> Variables { get; }

    /// <summary>
    /// Metadata for tracking and debugging.
    /// </summary>
    public ImmutableDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Current step being executed.
    /// </summary>
    public string? CurrentStepId { get; }

    /// <summary>
    /// Number of completed steps.
    /// </summary>
    public int CompletedStepCount { get; }

    /// <summary>
    /// Total number of steps in the pipeline.
    /// </summary>
    public int TotalStepCount { get; }

    /// <summary>
    /// Whether the pipeline should stop (e.g., due to error or early exit).
    /// </summary>
    public bool ShouldStop { get; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// The accumulated response text from LLM generation.
    /// </summary>
    public string ResponseText { get; }

    /// <summary>
    /// LLM generation parameters.
    /// </summary>
    public LlmParameters LlmParameters { get; }

    private PipelineContext(
        string executionId,
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        string conversationId,
        int? userId,
        DateTime startedAt,
        ImmutableList<ChatMessage> messages,
        string? systemPrompt,
        ImmutableList<ITool> tools,
        ImmutableList<ToolExecutionResult> toolResults,
        ImmutableDictionary<string, object> variables,
        ImmutableDictionary<string, object> metadata,
        string? currentStepId,
        int completedStepCount,
        int totalStepCount,
        bool shouldStop,
        string? errorMessage,
        string responseText,
        LlmParameters llmParameters)
    {
        ExecutionId = executionId;
        Pipeline = pipeline;
        Input = input;
        ConversationId = conversationId;
        UserId = userId;
        StartedAt = startedAt;
        Messages = messages;
        SystemPrompt = systemPrompt;
        Tools = tools;
        ToolResults = toolResults;
        Variables = variables;
        Metadata = metadata;
        CurrentStepId = currentStepId;
        CompletedStepCount = completedStepCount;
        TotalStepCount = totalStepCount;
        ShouldStop = shouldStop;
        ErrorMessage = errorMessage;
        ResponseText = responseText;
        LlmParameters = llmParameters;
    }

    /// <summary>
    /// Create a new pipeline context for an execution.
    /// </summary>
    public static PipelineContext Create(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        string conversationId,
        int? userId = null)
    {
        return new PipelineContext(
            executionId: Guid.NewGuid().ToString(),
            pipeline: pipeline,
            input: input,
            conversationId: conversationId,
            userId: userId,
            startedAt: DateTime.UtcNow,
            messages: ImmutableList<ChatMessage>.Empty,
            systemPrompt: null,
            tools: ImmutableList<ITool>.Empty,
            toolResults: ImmutableList<ToolExecutionResult>.Empty,
            variables: ImmutableDictionary<string, object>.Empty,
            metadata: ImmutableDictionary<string, object>.Empty,
            currentStepId: null,
            completedStepCount: 0,
            totalStepCount: pipeline.Steps.Count,
            shouldStop: false,
            errorMessage: null,
            responseText: string.Empty,
            llmParameters: new LlmParameters
            {
                Temperature = pipeline.Config.Temperature,
                MaxOutputTokens = pipeline.Config.MaxOutputTokens,
                MaxContextTokens = pipeline.Config.MaxContextTokens
            });
    }

    /// <summary>
    /// Create a copy with added messages.
    /// </summary>
    public PipelineContext WithMessages(IEnumerable<ChatMessage> messages)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages.AddRange(messages),
            SystemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with a new message added.
    /// </summary>
    public PipelineContext WithMessage(ChatMessage message)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages.Add(message),
            SystemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with system prompt set.
    /// </summary>
    public PipelineContext WithSystemPrompt(string systemPrompt)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, systemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with tools set.
    /// </summary>
    public PipelineContext WithTools(IEnumerable<ITool> tools)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, ImmutableList.CreateRange(tools), ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with a tool result added.
    /// </summary>
    public PipelineContext WithToolResult(ToolExecutionResult result)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults.Add(result), Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with a variable set.
    /// </summary>
    public PipelineContext WithVariable(string key, object value)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables.SetItem(key, value), Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with multiple variables set.
    /// </summary>
    public PipelineContext WithVariables(IEnumerable<KeyValuePair<string, object>> variables)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables.SetItems(variables), Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with metadata set.
    /// </summary>
    public PipelineContext WithMetadata(string key, object value)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables, Metadata.SetItem(key, value),
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with current step updated.
    /// </summary>
    public PipelineContext WithCurrentStep(string stepId)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables, Metadata,
            stepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with completed step count incremented.
    /// </summary>
    public PipelineContext WithStepCompleted()
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount + 1, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Create a copy with response text appended.
    /// </summary>
    public PipelineContext WithResponseText(string text)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText + text, LlmParameters);
    }

    /// <summary>
    /// Create a copy with response text replaced.
    /// </summary>
    public PipelineContext WithNewResponseText(string text)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, text, LlmParameters);
    }

    /// <summary>
    /// Create a copy with LLM parameters updated.
    /// </summary>
    public PipelineContext WithLlmParameters(LlmParameters parameters)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            ShouldStop, ErrorMessage, ResponseText, parameters);
    }

    /// <summary>
    /// Create a copy indicating execution should stop.
    /// </summary>
    public PipelineContext WithStop(string? errorMessage = null)
    {
        return new PipelineContext(
            ExecutionId, Pipeline, Input, ConversationId, UserId, StartedAt,
            Messages, SystemPrompt, Tools, ToolResults, Variables, Metadata,
            CurrentStepId, CompletedStepCount, TotalStepCount,
            true, errorMessage, ResponseText, LlmParameters);
    }

    /// <summary>
    /// Get a variable value with type conversion.
    /// </summary>
    public T? GetVariable<T>(string key)
    {
        if (Variables.TryGetValue(key, out var value))
        {
            if (value is T typed)
                return typed;
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    /// <summary>
    /// Check if a variable exists.
    /// </summary>
    public bool HasVariable(string key) => Variables.ContainsKey(key);

    /// <summary>
    /// Get execution duration.
    /// </summary>
    public TimeSpan Duration => DateTime.UtcNow - StartedAt;

    /// <summary>
    /// Get progress as a percentage (0-100).
    /// </summary>
    public int ProgressPercent => TotalStepCount > 0
        ? (int)Math.Round((double)CompletedStepCount / TotalStepCount * 100)
        : 0;
}

/// <summary>
/// LLM generation parameters.
/// </summary>
public sealed class LlmParameters
{
    public float Temperature { get; init; } = 0.7f;
    public int MaxOutputTokens { get; init; } = 2048;
    public int MaxContextTokens { get; init; } = 4096;
    public float TopP { get; init; } = 0.9f;
    public float RepetitionPenalty { get; init; } = 1.1f;
    public string[]? StopSequences { get; init; }
}

/// <summary>
/// Input to a pipeline execution.
/// </summary>
public sealed class PipelineInput
{
    /// <summary>
    /// The user's message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Conversation ID for tracking.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Previous conversation history.
    /// </summary>
    public IReadOnlyList<ChatMessage>? ConversationHistory { get; init; }

    /// <summary>
    /// Project context path if applicable.
    /// </summary>
    public string? ProjectPath { get; init; }

    /// <summary>
    /// Additional context data.
    /// </summary>
    public IReadOnlyDictionary<string, object>? AdditionalContext { get; init; }
}

/// <summary>
/// Result of a tool execution during pipeline.
/// </summary>
public sealed class ToolExecutionResult
{
    public required string ToolName { get; init; }
    public required string ToolCallId { get; init; }
    public required bool Success { get; init; }
    public required string Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, object>? Arguments { get; init; }
}
