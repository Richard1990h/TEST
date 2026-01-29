using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Pipeline.Storage;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.Services.Pipeline;

/// <summary>
/// Service for managing pipeline executions with logging and metrics.
/// </summary>
public interface IPipelineExecutionService
{
    /// <summary>
    /// Execute a pipeline with full logging and metrics.
    /// </summary>
    IAsyncEnumerable<PipelineStreamEvent> ExecuteWithLoggingAsync(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        string? conversationId,
        int? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the primary pipeline for execution.
    /// </summary>
    Task<PipelineDefinitionV2?> GetPipelineForMessageAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of pipeline execution service.
/// </summary>
public sealed class PipelineExecutionService : IPipelineExecutionService
{
    private readonly IPipelineEngine _pipelineEngine;
    private readonly IPipelineStore _pipelineStore;
    private readonly IPipelineExecutionStore _executionStore;
    private readonly ILogger<PipelineExecutionService> _logger;

    public PipelineExecutionService(
        IPipelineEngine pipelineEngine,
        IPipelineStore pipelineStore,
        IPipelineExecutionStore executionStore,
        ILogger<PipelineExecutionService> logger)
    {
        _pipelineEngine = pipelineEngine;
        _pipelineStore = pipelineStore;
        _executionStore = executionStore;
        _logger = logger;
    }

    public async IAsyncEnumerable<PipelineStreamEvent> ExecuteWithLoggingAsync(
        PipelineDefinitionV2 pipeline,
        PipelineInput input,
        string? conversationId,
        int? userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Start execution tracking
        var inputSummary = input.Message.Length > 500
            ? input.Message.Substring(0, 500)
            : input.Message;

        var executionId = await _executionStore.BeginExecutionAsync(
            pipeline.Id,
            conversationId,
            userId,
            inputSummary,
            pipeline.Steps.Count,
            cancellationToken);

        _logger.LogInformation("Starting pipeline execution {ExecutionId} for pipeline {PipelineId}",
            executionId, pipeline.Id);

        var startTime = DateTime.UtcNow;
        var completedSteps = 0;
        var toolCalls = 0;
        var success = true;
        string? errorMessage = null;
        string? outputSummary = null;

        try
        {
            var stepOrder = 0;

            await foreach (var evt in _pipelineEngine.ExecuteStreamingAsync(pipeline, input, cancellationToken))
            {
                // Track step completion
                if (evt.Type == PipelineStreamEventType.StepComplete)
                {
                    completedSteps++;
                    stepOrder++;

                    if (pipeline.Config.EnableExecutionLogs && evt.StepId != null)
                    {
                        await _executionStore.RecordStepAsync(
                            executionId,
                            evt.StepId,
                            "completed", // Could extract actual type from context
                            stepOrder,
                            true,
                            0, // Could track individual step duration
                            null,
                            evt.Content,
                            null,
                            cancellationToken);
                    }
                }

                // Track tool calls
                if (evt.Type == PipelineStreamEventType.ToolCall)
                {
                    toolCalls++;
                }

                // Track errors
                if (evt.Type == PipelineStreamEventType.Error)
                {
                    success = false;
                    errorMessage = evt.Content;
                }

                // Track completion
                if (evt.Type == PipelineStreamEventType.Complete)
                {
                    outputSummary = evt.Context?.ResponseText;
                    if (outputSummary?.Length > 500)
                    {
                        outputSummary = outputSummary.Substring(0, 500);
                    }
                }

                yield return evt;
            }
        }
        finally
        {
            var durationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Complete execution tracking
            await _executionStore.CompleteExecutionAsync(
                executionId,
                success,
                errorMessage,
                durationMs,
                completedSteps,
                outputSummary,
                cancellationToken);

            // Update metrics
            await _executionStore.UpdateMetricsAsync(
                pipeline.Id,
                DateTime.UtcNow,
                success,
                durationMs,
                completedSteps,
                toolCalls,
                cancellationToken);

            _logger.LogInformation(
                "Completed pipeline execution {ExecutionId}: Success={Success}, Duration={DurationMs}ms, Steps={Steps}",
                executionId, success, durationMs, completedSteps);
        }
    }

    public async Task<PipelineDefinitionV2?> GetPipelineForMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        return await _pipelineStore.GetForMessageAsync(message, cancellationToken);
    }
}
