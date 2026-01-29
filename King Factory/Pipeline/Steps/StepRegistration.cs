using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Pipeline.Steps.Control;
using LittleHelperAI.KingFactory.Pipeline.Steps.Inject;
using LittleHelperAI.KingFactory.Pipeline.Steps.LLM;
using LittleHelperAI.KingFactory.Pipeline.Steps.Tool;
using LittleHelperAI.KingFactory.Pipeline.Steps.Validate;
using Microsoft.Extensions.DependencyInjection;

namespace LittleHelperAI.KingFactory.Pipeline.Steps;

/// <summary>
/// Extension methods for registering pipeline steps.
/// </summary>
public static class StepRegistration
{
    /// <summary>
    /// Register all pipeline steps with DI.
    /// </summary>
    public static IServiceCollection AddPipelineSteps(this IServiceCollection services)
    {
        // Register step registry
        services.AddSingleton<IStepRegistry, StepRegistry>();

        // Register individual steps
        services.AddSingleton<IPipelineStep, InjectSystemPromptStep>();
        services.AddSingleton<IPipelineStep, InjectConversationStep>();
        services.AddSingleton<IPipelineStep, InjectToolsStep>();
        services.AddSingleton<IPipelineStep, InjectContextStep>();

        services.AddSingleton<IPipelineStep, LlmStreamStep>();
        services.AddSingleton<IPipelineStep, LlmStreamWithToolsStep>();
        services.AddSingleton<IPipelineStep, LlmClassifyStep>();

        services.AddSingleton<IPipelineStep, ToolParseStep>();
        services.AddSingleton<IPipelineStep, ToolExecuteStep>();
        services.AddSingleton<IPipelineStep, ToolLoopStep>();

        services.AddSingleton<IPipelineStep, BranchStep>();
        services.AddSingleton<IPipelineStep, LoopStep>();

        services.AddSingleton<IPipelineStep, ValidateOutputStep>();
        services.AddSingleton<IPipelineStep, ValidateCodeStep>();

        return services;
    }

    /// <summary>
    /// Initialize the step registry with all registered steps.
    /// </summary>
    public static IStepRegistry InitializeStepRegistry(this IServiceProvider services)
    {
        var registry = services.GetRequiredService<IStepRegistry>();
        var steps = services.GetServices<IPipelineStep>();

        registry.RegisterAll(steps);

        return registry;
    }
}
