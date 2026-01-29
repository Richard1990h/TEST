using System.Text;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.LLM;

/// <summary>
/// Streams LLM response without tool handling.
/// </summary>
public sealed class LlmStreamStep : PipelineStepBase
{
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly IPromptBuilder _promptBuilder;

    public override string TypeId => "llm.stream";
    public override string DisplayName => "LLM Stream";
    public override string Category => "LLM";
    public override string Description => "Generates a streaming response from the LLM without tool handling.";
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
            Description = "Maximum tokens to generate",
            DefaultValue = 2048,
            MinValue = 1,
            MaxValue = 32768
        },
        new StepParameterDefinition
        {
            Name = "codeMode",
            DisplayName = "Code Mode",
            Type = StepParameterType.Boolean,
            Description = "Enable code-focused generation mode",
            DefaultValue = false
        }
    );

    public LlmStreamStep(IUnifiedLlmProvider llmProvider, IPromptBuilder promptBuilder)
    {
        _llmProvider = llmProvider;
        _promptBuilder = promptBuilder;
    }

    public override async Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        // Build and execute non-streaming
        var prompt = BuildPrompt(context, config);
        var temperature = GetParameter<float>(config, "temperature", context.LlmParameters.Temperature);
        var maxTokens = GetParameter<int>(config, "maxTokens", context.LlmParameters.MaxOutputTokens);

        var response = await _llmProvider.GenerateAsync(prompt, maxTokens, temperature, cancellationToken);

        var newContext = context
            .WithNewResponseText(response)
            .WithMessage(new ChatMessage { Role = "assistant", Content = response });

        return Success(newContext, response);
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

        var prompt = BuildPrompt(context, config);
        var temperature = GetParameter<float>(config, "temperature", context.LlmParameters.Temperature);
        var maxTokens = GetParameter<int>(config, "maxTokens", context.LlmParameters.MaxOutputTokens);

        var responseBuilder = new StringBuilder();
        var currentContext = context;

        await foreach (var token in _llmProvider.StreamAsync(prompt, maxTokens, temperature, cancellationToken))
        {
            responseBuilder.Append(token);
            currentContext = currentContext.WithResponseText(token);

            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Token,
                StepId = config.StepId,
                Content = token,
                Context = currentContext
            };
        }

        // Add assistant message to context
        var response = responseBuilder.ToString();
        currentContext = currentContext
            .WithNewResponseText(response)
            .WithMessage(new ChatMessage { Role = "assistant", Content = response });

        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.StepComplete,
            StepId = config.StepId,
            Content = response,
            Context = currentContext
        };
    }

    private string BuildPrompt(PipelineContext context, StepConfiguration config)
    {
        var codeMode = GetParameter<bool>(config, "codeMode", false);

        return _promptBuilder.BuildPrompt(
            context.Messages.ToList(),
            new PromptContext
            {
                CodeMode = codeMode,
                SystemPromptOverride = context.SystemPrompt
            });
    }
}

/// <summary>
/// Helper interface for prompt building.
/// </summary>
public interface IPromptBuilder
{
    string BuildPrompt(IReadOnlyList<ChatMessage> messages, PromptContext? context = null);
}

/// <summary>
/// Context for prompt building.
/// </summary>
public class PromptContext
{
    public bool PlanningMode { get; set; }
    public bool CodeMode { get; set; }
    public bool FixMode { get; set; }
    public string? SystemPromptOverride { get; set; }
    public string? ToolsContext { get; set; }
    public string? TaskLock { get; set; }
    public string? DeveloperPrompt { get; set; }
    public bool SuppressToolDescriptions { get; set; }
}
