using LittleHelperAI.KingFactory.Context;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Intent;
using LittleHelperAI.KingFactory.Pipeline;
using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Pipeline.Steps;
using LittleHelperAI.KingFactory.Pipeline.Storage;
using LittleHelperAI.KingFactory.Prompts;
using LittleHelperAI.KingFactory.Reasoning;
using LittleHelperAI.KingFactory.State;
using LittleHelperAI.KingFactory.Tools;
using LittleHelperAI.KingFactory.Tools.Filesystem;
using LittleHelperAI.KingFactory.Tools.Network;
using LittleHelperAI.KingFactory.Tools.Shell;
using LittleHelperAI.KingFactory.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LittleHelperAI.KingFactory;

/// <summary>
/// Extension methods for registering Factory services.
/// </summary>
public static class FactoryServiceExtensions
{
    /// <summary>
    /// Add all Factory services to the service collection.
    /// </summary>
    public static IServiceCollection AddFactory(this IServiceCollection services, Action<FactoryConfiguration>? configure = null)
    {
        var config = new FactoryConfiguration();
        configure?.Invoke(config);

        // Register configuration
        services.AddSingleton(config.LlmConfig);
        services.AddSingleton(config.FilesystemConfig);
        services.AddSingleton(config.ShellConfig);
        services.AddSingleton(config.NetworkConfig);

        // Engine layer - Core components
        services.AddSingleton<IGpuDetector, GpuDetector>();
        services.AddSingleton<IModelDiscovery, ModelDiscovery>();
        services.AddSingleton<ILlmEngine, LlmEngine>();
        services.AddSingleton<IUnifiedLlmProvider, UnifiedLlmProvider>();

        // LLM Startup Service (auto-loads model on startup)
        services.AddSingleton<ILlmStartupService, LlmStartupService>();
        services.AddHostedService(sp => (LlmStartupService)sp.GetRequiredService<ILlmStartupService>());

        // Health monitoring and memory management
        services.AddSingleton<ILlmHealthMonitor, LlmHealthMonitor>();
        services.AddSingleton<ILlmMemoryManager, LlmMemoryManager>();

        // Prompts layer
        services.AddSingleton<ICorePrompt, CorePrompt>();
        services.AddSingleton<IPlanningPrompt, PlanningPrompt>();
        services.AddSingleton<IToolsPrompt, ToolsPromptImpl>();
        services.AddSingleton<IValidationPrompt, ValidationPrompt>();
        services.AddSingleton<IStreamingPrompt, StreamingPrompt>();
        services.AddSingleton<ICodePrompt, CodePrompt>();
        services.AddSingleton<IFixPrompt, FixPrompt>();
        services.AddSingleton<IRequirementsPrompt, RequirementsPrompt>();
        services.AddSingleton<ISystemPrompts, SystemPrompts>();

        // Tools layer
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolRouter, ToolRouter>();
        services.AddSingleton<IToolResultSanitizer, ToolResultSanitizer>();

        // File event notifier (default null implementation, override in backend for SignalR)
        services.AddSingleton<IFileEventNotifier, NullFileEventNotifier>();

        // Register individual tools
        services.AddSingleton<ITool, ReadFileTool>();
        services.AddSingleton<ITool, WriteFileTool>();
        services.AddSingleton<ITool, ListFilesTool>();
        services.AddSingleton<ITool, RunCommandTool>();
        services.AddSingleton<ITool, FetchTool>();

        // Pipeline layer (legacy)
        services.AddSingleton<IConversationManager, ConversationManager>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddSingleton<IResponseStreamer, ResponseStreamer>();
        services.AddSingleton<IMessageHandler, MessageHandler>();
        services.AddSingleton<IMessageHandlerFactory, MessageHandlerFactory>();
        services.AddSingleton<IPipelineRegistry, PipelineRegistry>();
        services.AddSingleton<IPipelineExecutor, PipelineExecutor>();

        // Pipeline V2 (new declarative engine)
        // Note: IPipelineStore and IPipelineExecutionStore are registered in Backend with EF Core implementations
        services.AddPipelineSteps();
        services.AddSingleton<IExecutionTracer, ExecutionTracer>();
        services.AddSingleton<IDependencyGraphBuilder, DependencyGraphBuilder>();
        services.AddSingleton<IStepExecutor, StepExecutor>();
        services.AddSingleton<IPipelineEngine, PipelineEngine>();

        // Intent layer
        services.AddSingleton<IIntentClassifier, IntentClassifier>();
        services.AddSingleton<IBuildSignalDetector, BuildSignalDetector>();
        services.AddSingleton<IScopeExtractor, ScopeExtractor>();

        // Reasoning layer
        services.AddSingleton<ITaskDecomposer, TaskDecomposer>();
        services.AddSingleton<IReasoningLoop, ReasoningLoop>();

        // Validation layer
        services.AddSingleton<IValidationPass, ValidationPass>();
        services.AddSingleton<IResultReflection, ResultReflection>();
        services.AddSingleton<IPipelineValidationRules, PipelineValidationRulesRegistry>();

        // State layer
        services.AddSingleton<IProjectState, ProjectState>();
        services.AddSingleton<IProjectDetector, ProjectDetector>();
        services.AddSingleton<IFileDiffEngine, FileDiffEngine>();

        // Context layer
        services.AddSingleton<IMessageWindowing, MessageWindowing>();
        services.AddSingleton<IContextSummarizer, ContextSummarizer>();
        services.AddSingleton<IConversationSummarizationTrigger, ConversationSummarizationTrigger>();

        // Main Factory
        services.AddSingleton<IFactory, Factory>();

        // HTTP client for fetch tool
        services.AddHttpClient("FactoryFetch", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LittleHelperAI/1.0");
        });

        return services;
    }

    /// <summary>
    /// Add Factory with default configuration.
    /// </summary>
    public static IServiceCollection AddFactory(this IServiceCollection services, string modelPath)
    {
        return services.AddFactory(config =>
        {
            config.LlmConfig.ModelPath = modelPath;
        });
    }
}

/// <summary>
/// Configuration for the Factory system.
/// </summary>
public class FactoryConfiguration
{
    /// <summary>
    /// LLM engine configuration.
    /// </summary>
    public LlmConfig LlmConfig { get; set; } = new();

    /// <summary>
    /// Filesystem tools configuration.
    /// </summary>
    public FilesystemConfig FilesystemConfig { get; set; } = new();

    /// <summary>
    /// Shell tools configuration.
    /// </summary>
    public ShellConfig ShellConfig { get; set; } = new();

    /// <summary>
    /// Network tools configuration.
    /// </summary>
    public NetworkConfig NetworkConfig { get; set; } = new();
}
