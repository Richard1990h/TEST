using LittleHelperAI.KingFactory.Context;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Intent;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Pipeline;
using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Pipeline.Storage;
using LittleHelperAI.KingFactory.Reasoning;
using LittleHelperAI.KingFactory.State;
using LittleHelperAI.KingFactory.Tools;
using LittleHelperAI.KingFactory.Validation;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory;

/// <summary>
/// Main entry point for the AI Factory system.
/// Orchestrates all components for processing user requests.
/// </summary>
public interface IFactory
{
    /// <summary>
    /// Process a user message and stream the response.
    /// </summary>
    IAsyncEnumerable<FactoryOutput> ProcessAsync(string message, FactoryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a message in a specific conversation.
    /// </summary>
    IAsyncEnumerable<FactoryOutput> ProcessInConversationAsync(string conversationId, string message, FactoryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the LLM model.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the factory is initialized and ready.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Get the current project state.
    /// </summary>
    IProjectState ProjectState { get; }

    /// <summary>
    /// Get conversation manager.
    /// </summary>
    IConversationManager Conversations { get; }
}

/// <summary>
/// Options for factory processing.
/// </summary>
public class FactoryOptions
{
    /// <summary>
    /// Enable tool usage.
    /// </summary>
    public bool EnableTools { get; set; } = true;

    /// <summary>
    /// Enable reasoning loop for complex tasks.
    /// </summary>
    public bool EnableReasoning { get; set; } = true;

    /// <summary>
    /// Enable validation of outputs.
    /// </summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>
    /// Maximum tokens in response.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Maximum context window tokens.
    /// </summary>
    public int MaxContextTokens { get; set; } = 4096;

    /// <summary>
    /// Temperature for LLM generation.
    /// </summary>
    public float Temperature { get; set; } = 0.7f;
}

/// <summary>
/// Output from the factory.
/// </summary>
public class FactoryOutput
{
    /// <summary>
    /// Type of output.
    /// </summary>
    public FactoryOutputType Type { get; set; }

    /// <summary>
    /// Content of the output.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a partial (streaming) output.
    /// </summary>
    public bool IsPartial { get; set; }

    /// <summary>
    /// Whether this is the final output.
    /// </summary>
    public bool IsFinal { get; set; }

    /// <summary>
    /// Associated metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Types of factory output.
/// </summary>
public enum FactoryOutputType
{
    /// <summary>
    /// Streaming text token.
    /// </summary>
    Token,

    /// <summary>
    /// Thinking/reasoning output.
    /// </summary>
    Thinking,

    /// <summary>
    /// Plan being created.
    /// </summary>
    Planning,

    /// <summary>
    /// Tool being called.
    /// </summary>
    ToolCall,

    /// <summary>
    /// Result from tool execution.
    /// </summary>
    ToolResult,

    /// <summary>
    /// Progress update.
    /// </summary>
    Progress,

    /// <summary>
    /// Error message.
    /// </summary>
    Error,

    /// <summary>
    /// Complete response.
    /// </summary>
    Complete
}

/// <summary>
/// Main Factory implementation.
/// </summary>
public sealed class Factory : IFactory, IDisposable
{
    private readonly ILogger<Factory> _logger;
    private readonly ILlmEngine _llmEngine;
    private readonly ILlmStartupService _startupService;
    private readonly ILlmHealthMonitor _healthMonitor;
    private readonly IMessageHandler _messageHandler;
    private readonly IPipelineExecutor _pipelineExecutor;
    private readonly IIntentClassifier _intentClassifier;
    private readonly IReasoningLoop _reasoningLoop;
    private readonly IToolRouter _toolRouter;
    private readonly IValidationPass _validationPass;
    private readonly IProjectState _projectState;
    private readonly IConversationManager _conversationManager;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IContextSummarizer _contextSummarizer;

    // Pipeline V2 components
    private readonly IPipelineEngine? _pipelineEngineV2;
    private readonly IPipelineStore? _pipelineStore;
    private readonly LlmConfig _llmConfig;

    private bool _isReady;
    private bool _disposed;

    public bool IsReady => _isReady && _llmEngine.IsLoaded;
    public IProjectState ProjectState => _projectState;
    public IConversationManager Conversations => _conversationManager;

    /// <summary>
    /// Get current LLM initialization status.
    /// </summary>
    public LlmInitStatus InitStatus => _startupService.Status;

    /// <summary>
    /// Get initialization progress (0-100).
    /// </summary>
    public int InitProgress => _startupService.Progress;

    /// <summary>
    /// Get current status message.
    /// </summary>
    public string StatusMessage => _startupService.StatusMessage;

    /// <summary>
    /// Get detailed initialization info.
    /// </summary>
    public LlmInitInfo GetInitInfo() => _startupService.GetDetailedStatus();

    /// <summary>
    /// Get health report.
    /// </summary>
    public LlmHealthReport GetHealthReport() => _healthMonitor.GetHealthReport();

    public Factory(
        ILogger<Factory> logger,
        ILlmEngine llmEngine,
        ILlmStartupService startupService,
        ILlmHealthMonitor healthMonitor,
        IMessageHandler messageHandler,
        IPipelineExecutor pipelineExecutor,
        IIntentClassifier intentClassifier,
        IReasoningLoop reasoningLoop,
        IToolRouter toolRouter,
        IValidationPass validationPass,
        IProjectState projectState,
        IConversationManager conversationManager,
        IPromptBuilder promptBuilder,
        IContextSummarizer contextSummarizer,
        LlmConfig llmConfig,
        IPipelineEngine? pipelineEngineV2 = null,
        IPipelineStore? pipelineStore = null)
    {
        _logger = logger;
        _llmEngine = llmEngine;
        _startupService = startupService;
        _healthMonitor = healthMonitor;
        _messageHandler = messageHandler;
        _pipelineExecutor = pipelineExecutor;
        _intentClassifier = intentClassifier;
        _reasoningLoop = reasoningLoop;
        _toolRouter = toolRouter;
        _validationPass = validationPass;
        _projectState = projectState;
        _conversationManager = conversationManager;
        _promptBuilder = promptBuilder;
        _contextSummarizer = contextSummarizer;
        _llmConfig = llmConfig;
        _pipelineEngineV2 = pipelineEngineV2;
        _pipelineStore = pipelineStore;

        // Don't start health monitoring here - wait until initialization is complete
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isReady && _llmEngine.IsLoaded)
            return;

        _logger.LogInformation("Initializing Factory via startup service...");

        try
        {
            // Wait for the startup service to complete initialization
            // The startup service handles: GPU detection, model discovery, validation, loading, warmup
            await _startupService.WaitForReadyAsync(cancellationToken);

            var status = _startupService.Status;
            if (status == LlmInitStatus.Ready)
            {
                _isReady = true;
                _logger.LogInformation("Factory initialized successfully via startup service");

                // Log detailed info
                var info = _startupService.GetDetailedStatus();
                _logger.LogInformation("Model: {Model}, GPU: {Gpu}, Layers: {Layers}",
                    info.SelectedModel?.FileName ?? "Unknown",
                    info.SelectedGpu?.Name ?? "CPU",
                    info.GpuLayers);

                // Start health monitoring now that model is loaded
                _healthMonitor.StartMonitoring(TimeSpan.FromSeconds(30));
            }
            else if (status == LlmInitStatus.NoModelFound)
            {
                _logger.LogWarning("No model found during initialization");
                _isReady = false;
            }
            else
            {
                throw new InvalidOperationException($"Initialization failed with status: {status}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Factory");
            throw;
        }
    }

    public IAsyncEnumerable<FactoryOutput> ProcessAsync(string message, FactoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var conversationId = Guid.NewGuid().ToString();
        return ProcessInConversationAsync(conversationId, message, options, cancellationToken);
    }

    public async IAsyncEnumerable<FactoryOutput> ProcessInConversationAsync(
        string conversationId,
        string message,
        FactoryOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new FactoryOptions();

        if (!_isReady)
        {
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Error,
                Content = "Factory not initialized. Call InitializeAsync first.",
                IsFinal = true
            };
            yield break;
        }

        _logger.LogInformation("Processing message in conversation {ConversationId}", conversationId);

        // Try Pipeline V2 first if available and configured
        if (_llmConfig.UsePipelineV2 && _pipelineEngineV2 != null && _pipelineStore != null)
        {
            var pipelineV2 = await _pipelineStore.GetForMessageAsync(message, cancellationToken);
            if (pipelineV2 != null)
            {
                _logger.LogInformation("Using Pipeline V2: {PipelineName} for conversation {ConversationId}",
                    pipelineV2.Name, conversationId);

                var input = new PipelineInput
                {
                    Message = message,
                    ConversationId = conversationId,
                    ProjectPath = _projectState.WorkingDirectory
                };

                await foreach (var evt in _pipelineEngineV2.ExecuteStreamingAsync(pipelineV2, input, cancellationToken))
                {
                    yield return MapPipelineV2Event(evt);
                }
                yield break;
            }
        }

        // Fall back to legacy pipeline executor
        if (_pipelineExecutor != null)
        {
            await foreach (var output in _pipelineExecutor.ProcessAsync(conversationId, message, cancellationToken))
            {
                yield return output;
            }
            yield break;
        }

        // Classify intent
        var intent = await _intentClassifier.ClassifyAsync(message, cancellationToken);

        _logger.LogDebug("Intent: {Intent} ({Confidence})", intent.Intent, intent.Confidence);

        // Determine processing mode
        var useReasoning = options.EnableReasoning &&
            intent.Intent == IntentType.Planning &&
            intent.Confidence >= 0.8;

        if (useReasoning)
        {
            // Use reasoning loop for complex tasks
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Thinking,
                Content = "Analyzing request...",
                IsPartial = true
            };

            await foreach (var output in _reasoningLoop.ExecuteAsync(message, cancellationToken))
            {
                yield return MapReasoningOutput(output);
            }
        }
        else if (options.EnableTools && NeedsTools(intent))
        {
            // Use message handler with tools (structured output)
            await foreach (var output in _messageHandler.ProcessWithToolsStructuredAsync(conversationId, message, null, cancellationToken))
            {
                yield return MapMessageOutput(output);
            }
        }
        else
        {
            // Simple processing
            var fullResponse = new System.Text.StringBuilder();

            await foreach (var token in _messageHandler.ProcessAsync(conversationId, message, null, cancellationToken))
            {
                fullResponse.Append(token);

                yield return new FactoryOutput
                {
                    Type = FactoryOutputType.Token,
                    Content = token,
                    IsPartial = true
                };
            }

            // Validate if enabled
            if (options.EnableValidation)
            {
                var validation = _validationPass.Validate(fullResponse.ToString(), new ValidationContext
                {
                    OriginalQuery = message
                });

                if (!validation.IsValid)
                {
                    yield return new FactoryOutput
                    {
                        Type = FactoryOutputType.Error,
                        Content = string.Join("; ", validation.Issues.Select(i => i.Message)),
                        IsPartial = true
                    };
                }
            }

            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Complete,
                Content = fullResponse.ToString(),
                IsFinal = true
            };
        }
    }

    private bool NeedsTools(IntentResult intent)
    {
        return intent.Intent switch
        {
            IntentType.CodeRead or IntentType.CodeWrite or IntentType.CodeEdit => true,
            IntentType.FileList or IntentType.FileCreate or IntentType.FileDelete => true,
            IntentType.ShellCommand => true,
            IntentType.Search => true,
            _ => false
        };
    }

    private FactoryOutput MapReasoningOutput(ReasoningOutput output)
    {
        return new FactoryOutput
        {
            Type = output.Type switch
            {
                ReasoningOutputType.Thinking => FactoryOutputType.Thinking,
                ReasoningOutputType.Planning => FactoryOutputType.Planning,
                ReasoningOutputType.ToolCall => FactoryOutputType.ToolCall,
                ReasoningOutputType.ToolResult => FactoryOutputType.ToolResult,
                ReasoningOutputType.Progress => FactoryOutputType.Progress,
                ReasoningOutputType.Response => FactoryOutputType.Token,
                ReasoningOutputType.Error => FactoryOutputType.Error,
                ReasoningOutputType.Complete => FactoryOutputType.Complete,
                _ => FactoryOutputType.Token
            },
            Content = output.Content,
            IsPartial = !output.IsFinal,
            IsFinal = output.IsFinal,
            Metadata = output.CurrentStep != null
                ? new Dictionary<string, object>
                {
                    ["step"] = output.CurrentStep.StepNumber,
                    ["progress"] = output.Progress
                }
                : null
        };
    }

    private FactoryOutput MapMessageOutput(MessageOutput output)
    {
        return new FactoryOutput
        {
            Type = output.Type switch
            {
                MessageOutputType.Token => FactoryOutputType.Token,
                MessageOutputType.ToolCall => FactoryOutputType.ToolCall,
                MessageOutputType.ToolResult => FactoryOutputType.ToolResult,
                MessageOutputType.Status => FactoryOutputType.Progress,
                MessageOutputType.Error => FactoryOutputType.Error,
                MessageOutputType.Complete => FactoryOutputType.Complete,
                _ => FactoryOutputType.Token
            },
            Content = output.Content,
            IsPartial = !output.IsFinal,
            IsFinal = output.IsFinal,
            Metadata = output.ToolName != null
                ? new Dictionary<string, object>
                {
                    ["toolName"] = output.ToolName,
                    ["toolSuccess"] = output.ToolSuccess ?? false,
                    ["toolArguments"] = output.ToolArguments ?? new Dictionary<string, object>()
                }
                : null
        };
    }

    private FactoryOutput MapPipelineV2Event(PipelineStreamEvent evt)
    {
        return new FactoryOutput
        {
            Type = evt.Type switch
            {
                PipelineStreamEventType.Token => FactoryOutputType.Token,
                PipelineStreamEventType.StepStarted => FactoryOutputType.Progress,
                PipelineStreamEventType.StepComplete => FactoryOutputType.Progress,
                PipelineStreamEventType.Progress => FactoryOutputType.Progress,
                PipelineStreamEventType.ToolCall => FactoryOutputType.ToolCall,
                PipelineStreamEventType.ToolResult => FactoryOutputType.ToolResult,
                PipelineStreamEventType.Error => FactoryOutputType.Error,
                PipelineStreamEventType.Complete => FactoryOutputType.Complete,
                PipelineStreamEventType.Started => FactoryOutputType.Progress,
                PipelineStreamEventType.Warning => FactoryOutputType.Error,
                _ => FactoryOutputType.Token
            },
            Content = evt.Content ?? string.Empty,
            IsPartial = evt.Type != PipelineStreamEventType.Complete,
            IsFinal = evt.Type == PipelineStreamEventType.Complete,
            Metadata = evt.StepId != null
                ? new Dictionary<string, object>
                {
                    ["stepId"] = evt.StepId,
                    ["stepType"] = evt.Type.ToString()
                }
                : null
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop health monitoring
        _healthMonitor.StopMonitoring();

        if (_llmEngine is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (_healthMonitor is IDisposable healthDisposable)
        {
            healthDisposable.Dispose();
        }
    }
}
