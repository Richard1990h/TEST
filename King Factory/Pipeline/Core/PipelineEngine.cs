using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// Main entry point for the V2 pipeline execution engine.
/// Executes declarative pipelines step-by-step with full tracing and streaming support.
/// </summary>
public interface IPipelineEngine
{
    /// <summary>
    /// Execute a pipeline with streaming output.
    /// </summary>
    IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a pipeline and return the final context.
    /// </summary>
    Task<PipelineExecutionResult> ExecuteAsync(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a pipeline definition.
    /// </summary>
    PipelineValidationResult ValidatePipeline(PipelineDefinitionV2 pipeline);
}

/// <summary>
/// Default implementation of the pipeline engine.
/// </summary>
public sealed class PipelineEngine : IPipelineEngine
{
    private readonly ILogger<PipelineEngine> _logger;
    private readonly IStepRegistry _stepRegistry;
    private readonly IStepExecutor _stepExecutor;
    private readonly IDependencyGraphBuilder _graphBuilder;
    private readonly IExecutionTracer _tracer;

    public PipelineEngine(
        ILogger<PipelineEngine> logger,
        IStepRegistry stepRegistry,
        IStepExecutor stepExecutor,
        IDependencyGraphBuilder graphBuilder,
        IExecutionTracer tracer)
    {
        _logger = logger;
        _stepRegistry = stepRegistry;
        _stepExecutor = stepExecutor;
        _graphBuilder = graphBuilder;
        _tracer = tracer;
    }

    public IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<PipelineStreamEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        // Start execution in background
        _ = ExecuteInternalAsync(pipeline, input, channel.Writer, cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task ExecuteInternalAsync(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        ChannelWriter<PipelineStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        PipelineContext? context = null;
        ExecutionTrace? trace = null;

        try
        {
            // Validate pipeline
            var validation = ValidatePipeline(pipeline);
            if (!validation.IsValid)
            {
                await writer.WriteAsync(new PipelineStreamEvent
                {
                    Type = PipelineStreamEventType.Error,
                    Content = $"Pipeline validation failed: {string.Join("; ", validation.Errors)}"
                }, cancellationToken);
                return;
            }

            // Build dependency graph
            var graph = _graphBuilder.Build(pipeline);
            var graphValidation = _graphBuilder.Validate(graph);
            if (!graphValidation.IsValid)
            {
                await writer.WriteAsync(new PipelineStreamEvent
                {
                    Type = PipelineStreamEventType.Error,
                    Content = $"Dependency graph invalid: {string.Join("; ", graphValidation.Errors)}"
                }, cancellationToken);
                return;
            }

            // Create initial context
            var conversationId = input.ConversationId ?? Guid.NewGuid().ToString();
            context = PipelineContext.Create(pipeline, input, conversationId);

            // Start trace
            trace = _tracer.BeginExecution(pipeline.Id, pipeline.Name, conversationId);

            await writer.WriteAsync(new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Started,
                Context = context,
                Metadata = new Dictionary<string, object>
                {
                    ["executionId"] = context.ExecutionId,
                    ["pipelineId"] = pipeline.Id,
                    ["stepCount"] = pipeline.Steps.Count
                }
            }, cancellationToken);

            var completedSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stepConfigs = pipeline.Steps.ToDictionary(s => s.Id, s => s.ToConfiguration(), StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Starting pipeline {PipelineId} execution {ExecutionId} with {StepCount} steps",
                pipeline.Id, context.ExecutionId, pipeline.Steps.Count);

            // Execute steps in order
            foreach (var stepId in graph.ExecutionOrder)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    trace.Complete(false, "Cancelled");
                    await writer.WriteAsync(new PipelineStreamEvent
                    {
                        Type = PipelineStreamEventType.Error,
                        Content = "Pipeline execution cancelled",
                        Context = context
                    }, cancellationToken);
                    return;
                }

                if (context.ShouldStop)
                {
                    _logger.LogInformation("Pipeline stopped at step {StepId}: {Reason}", stepId, context.ErrorMessage);
                    break;
                }

                // Wait for dependencies
                if (!graph.AreDependenciesComplete(stepId, completedSteps))
                {
                    _logger.LogWarning("Step {StepId} dependencies not complete - this shouldn't happen with proper ordering", stepId);
                    continue;
                }

                if (!stepConfigs.TryGetValue(stepId, out var config))
                {
                    _logger.LogWarning("Step config not found for {StepId}", stepId);
                    continue;
                }

                // Get step implementation
                var step = _stepRegistry.GetStep(config.TypeId);
                if (step == null)
                {
                    _logger.LogError("Unknown step type: {StepType}", config.TypeId);
                    await writer.WriteAsync(new PipelineStreamEvent
                    {
                        Type = PipelineStreamEventType.Error,
                        StepId = stepId,
                        Content = $"Unknown step type: {config.TypeId}",
                        Context = context
                    }, cancellationToken);

                    if (!config.ContinueOnError)
                    {
                        trace.Complete(false, $"Unknown step type: {config.TypeId}");
                        return;
                    }
                    continue;
                }

                // Update context with current step
                context = context.WithCurrentStep(stepId);

                // Execute step with streaming
                if (step.SupportsStreaming)
                {
                    var shouldBreak = false;
                    await foreach (var evt in _stepExecutor.ExecuteStreamingAsync(step, context, config, cancellationToken))
                    {
                        // Update context from event if provided
                        if (evt.Context != null)
                        {
                            context = evt.Context;
                        }

                        await writer.WriteAsync(evt, cancellationToken);

                        if (evt.Type == PipelineStreamEventType.Error && !config.ContinueOnError)
                        {
                            trace.Complete(false, evt.Content);
                            shouldBreak = true;
                            break;
                        }
                    }

                    if (shouldBreak)
                        return;
                }
                else
                {
                    // Non-streaming step
                    await writer.WriteAsync(new PipelineStreamEvent
                    {
                        Type = PipelineStreamEventType.StepStarted,
                        StepId = stepId,
                        Context = context
                    }, cancellationToken);

                    var result = await _stepExecutor.ExecuteAsync(step, context, config, cancellationToken);
                    context = result.Context;

                    if (result.Success)
                    {
                        await writer.WriteAsync(new PipelineStreamEvent
                        {
                            Type = PipelineStreamEventType.StepComplete,
                            StepId = stepId,
                            Content = result.Output,
                            Context = context
                        }, cancellationToken);
                    }
                    else
                    {
                        await writer.WriteAsync(new PipelineStreamEvent
                        {
                            Type = PipelineStreamEventType.Error,
                            StepId = stepId,
                            Content = result.ErrorMessage,
                            Context = context
                        }, cancellationToken);

                        if (!config.ContinueOnError)
                        {
                            trace.Complete(false, result.ErrorMessage);
                            return;
                        }
                    }
                }

                // Mark step as completed
                completedSteps.Add(stepId);
                context = context.WithStepCompleted();

                // Emit progress
                await writer.WriteAsync(new PipelineStreamEvent
                {
                    Type = PipelineStreamEventType.Progress,
                    StepId = stepId,
                    Context = context,
                    Metadata = new Dictionary<string, object>
                    {
                        ["completedSteps"] = context.CompletedStepCount,
                        ["totalSteps"] = context.TotalStepCount,
                        ["progressPercent"] = context.ProgressPercent
                    }
                }, cancellationToken);
            }

            // Pipeline complete
            trace.Complete(true);

            _logger.LogInformation("Pipeline {PipelineId} execution {ExecutionId} completed in {Duration}ms",
                pipeline.Id, context.ExecutionId, context.Duration.TotalMilliseconds);

            await writer.WriteAsync(new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Complete,
                Context = context,
                Metadata = new Dictionary<string, object>
                {
                    ["executionId"] = context.ExecutionId,
                    ["durationMs"] = context.Duration.TotalMilliseconds,
                    ["completedSteps"] = context.CompletedStepCount
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            trace?.Complete(false, "Cancelled");
            _logger.LogInformation("Pipeline {PipelineId} execution {ExecutionId} cancelled",
                pipeline.Id, context?.ExecutionId ?? "unknown");

            await writer.WriteAsync(new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Error,
                Content = "Pipeline execution cancelled",
                Context = context
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            trace?.Complete(false, ex.Message);
            _logger.LogError(ex, "Pipeline {PipelineId} execution {ExecutionId} failed",
                pipeline.Id, context?.ExecutionId ?? "unknown");

            await writer.WriteAsync(new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Error,
                Content = $"Pipeline execution failed: {ex.Message}",
                Context = context
            }, CancellationToken.None);
        }
        finally
        {
            writer.Complete();
        }
    }

    public async Task<PipelineExecutionResult> ExecuteAsync(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        CancellationToken cancellationToken = default)
    {
        PipelineContext? finalContext = null;
        var events = new List<PipelineStreamEvent>();
        string? errorMessage = null;

        await foreach (var evt in ExecuteStreamingAsync(pipeline, input, cancellationToken))
        {
            events.Add(evt);

            if (evt.Context != null)
            {
                finalContext = evt.Context;
            }

            if (evt.Type == PipelineStreamEventType.Error)
            {
                errorMessage = evt.Content;
            }
        }

        var success = errorMessage == null && finalContext != null && !finalContext.ShouldStop;

        return new PipelineExecutionResult
        {
            Success = success,
            Context = finalContext,
            ErrorMessage = errorMessage ?? finalContext?.ErrorMessage,
            Events = events,
            DurationMs = finalContext?.Duration.TotalMilliseconds ?? 0
        };
    }

    public PipelineValidationResult ValidatePipeline(PipelineDefinitionV2 pipeline)
    {
        var result = pipeline.Validate();
        var errors = result.Errors.ToList();
        var warnings = result.Warnings.ToList();

        // Validate that all step types exist
        foreach (var step in pipeline.Steps)
        {
            if (!_stepRegistry.HasStep(step.Type))
            {
                errors.Add($"Step {step.Id} uses unknown type: {step.Type}");
            }
        }

        return new PipelineValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}

/// <summary>
/// Result of a complete pipeline execution.
/// </summary>
public sealed class PipelineExecutionResult
{
    /// <summary>
    /// Whether the pipeline executed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Final pipeline context.
    /// </summary>
    public PipelineContext? Context { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// All events from the execution.
    /// </summary>
    public IReadOnlyList<PipelineStreamEvent> Events { get; init; } = Array.Empty<PipelineStreamEvent>();

    /// <summary>
    /// Total execution time in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// Get the response text from the execution.
    /// </summary>
    public string ResponseText => Context?.ResponseText ?? string.Empty;

    /// <summary>
    /// Get all token events as concatenated text.
    /// </summary>
    public string GetTokenOutput()
    {
        return string.Concat(Events
            .Where(e => e.Type == PipelineStreamEventType.Token)
            .Select(e => e.Content ?? string.Empty));
    }
}

/// <summary>
/// Factory for creating the pipeline engine.
/// </summary>
public static class PipelineEngineFactory
{
    /// <summary>
    /// Create a pipeline engine with all dependencies.
    /// </summary>
    public static IPipelineEngine Create(
        ILoggerFactory loggerFactory,
        IStepRegistry stepRegistry,
        IExecutionTracer? tracer = null)
    {
        var engineLogger = loggerFactory.CreateLogger<PipelineEngine>();
        var executorLogger = loggerFactory.CreateLogger<StepExecutor>();
        var tracerInstance = tracer ?? new ExecutionTracer(loggerFactory.CreateLogger<ExecutionTracer>());

        var stepExecutor = new StepExecutor(executorLogger, tracerInstance);
        var graphBuilder = new DependencyGraphBuilder();

        return new PipelineEngine(
            engineLogger,
            stepRegistry,
            stepExecutor,
            graphBuilder,
            tracerInstance);
    }
}
