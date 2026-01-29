using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// Executes individual pipeline steps with retry, timeout, and error handling.
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// Execute a step with retry and timeout handling.
    /// </summary>
    Task<StepExecutionResult> ExecuteAsync(
        IPipelineStep step,
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken);

    /// <summary>
    /// Execute a step with streaming output.
    /// </summary>
    IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        IPipelineStep step,
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default step executor with retry and timeout support.
/// </summary>
public sealed class StepExecutor : IStepExecutor
{
    private readonly ILogger<StepExecutor> _logger;
    private readonly IExecutionTracer _tracer;
    private readonly StepExecutorOptions _options;

    public StepExecutor(
        ILogger<StepExecutor> logger,
        IExecutionTracer tracer,
        StepExecutorOptions? options = null)
    {
        _logger = logger;
        _tracer = tracer;
        _options = options ?? new StepExecutorOptions();
    }

    public async Task<StepExecutionResult> ExecuteAsync(
        IPipelineStep step,
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!config.Enabled)
        {
            _logger.LogDebug("Step {StepId} is disabled, skipping", config.StepId);
            return StepExecutionResult.Succeeded(context, "Step disabled");
        }

        // Check condition if specified
        if (!string.IsNullOrEmpty(config.Condition) && !EvaluateCondition(context, config.Condition))
        {
            _logger.LogDebug("Step {StepId} condition not met, skipping", config.StepId);
            return StepExecutionResult.Succeeded(context, "Condition not met");
        }

        var stopwatch = Stopwatch.StartNew();
        var stepTrace = _tracer.BeginStep(context.ExecutionId, config.StepId, step.TypeId);

        try
        {
            _logger.LogDebug("Executing step {StepId} ({StepType})", config.StepId, step.TypeId);

            // Validate configuration
            var validation = step.Validate(config);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors);
                stepTrace.Complete(false, $"Validation failed: {errors}");
                return StepExecutionResult.Failed(context, $"Step validation failed: {errors}");
            }

            // Build execution policy (timeout + retry)
            var timeoutMs = config.TimeoutMs > 0 ? config.TimeoutMs : _options.DefaultTimeoutMs;
            var retryCount = config.RetryCount > 0 ? config.RetryCount : _options.DefaultRetryCount;

            StepExecutionResult result;

            if (retryCount > 0)
            {
                result = await ExecuteWithRetryAsync(step, context, config, timeoutMs, retryCount, cancellationToken);
            }
            else if (timeoutMs > 0)
            {
                result = await ExecuteWithTimeoutAsync(step, context, config, timeoutMs, cancellationToken);
            }
            else
            {
                result = await step.ExecuteAsync(context, config, cancellationToken);
            }

            stopwatch.Stop();

            // Update result with duration
            result = new StepExecutionResult
            {
                Success = result.Success,
                Context = result.Context,
                Output = result.Output,
                ErrorMessage = result.ErrorMessage,
                Duration = stopwatch.Elapsed,
                Metadata = result.Metadata
            };

            stepTrace.Complete(result.Success, result.ErrorMessage);

            if (result.Success)
            {
                _logger.LogDebug("Step {StepId} completed in {Duration}ms", config.StepId, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning("Step {StepId} failed: {Error}", config.StepId, result.ErrorMessage);

                if (config.ContinueOnError)
                {
                    _logger.LogInformation("Continuing pipeline despite step {StepId} failure (ContinueOnError=true)", config.StepId);
                    return new StepExecutionResult
                    {
                        Success = true,
                        Context = context.WithMetadata($"step.{config.StepId}.error", result.ErrorMessage ?? "Unknown error"),
                        Duration = stopwatch.Elapsed,
                        Metadata = result.Metadata
                    };
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            stepTrace.Complete(false, "Cancelled");
            _logger.LogInformation("Step {StepId} cancelled", config.StepId);
            throw;
        }
        catch (TimeoutRejectedException ex)
        {
            stopwatch.Stop();
            stepTrace.Complete(false, "Timeout");
            _logger.LogWarning("Step {StepId} timed out after {Duration}ms", config.StepId, stopwatch.ElapsedMilliseconds);

            if (config.ContinueOnError)
            {
                return StepExecutionResult.Succeeded(
                    context.WithMetadata($"step.{config.StepId}.timeout", true),
                    "Timed out");
            }

            return StepExecutionResult.Failed(context, $"Step timed out: {ex.Message}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            stepTrace.Complete(false, ex.Message);
            _logger.LogError(ex, "Step {StepId} threw exception", config.StepId);

            if (config.ContinueOnError)
            {
                return StepExecutionResult.Succeeded(
                    context.WithMetadata($"step.{config.StepId}.exception", ex.Message),
                    $"Exception: {ex.Message}");
            }

            return StepExecutionResult.Failed(context, $"Step threw exception: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<PipelineStreamEvent> ExecuteStreamingAsync(
        IPipelineStep step,
        PipelineContext context,
        StepConfiguration config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!config.Enabled)
        {
            _logger.LogDebug("Step {StepId} is disabled, skipping", config.StepId);
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.StepComplete,
                StepId = config.StepId,
                Content = "Step disabled",
                Context = context
            };
            yield break;
        }

        // Check condition
        if (!string.IsNullOrEmpty(config.Condition) && !EvaluateCondition(context, config.Condition))
        {
            _logger.LogDebug("Step {StepId} condition not met, skipping", config.StepId);
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.StepComplete,
                StepId = config.StepId,
                Content = "Condition not met",
                Context = context
            };
            yield break;
        }

        var stepTrace = _tracer.BeginStep(context.ExecutionId, config.StepId, step.TypeId);

        // Validate
        var validation = step.Validate(config);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors);
            stepTrace.Complete(false, $"Validation failed: {errors}");
            yield return new PipelineStreamEvent
            {
                Type = PipelineStreamEventType.Error,
                StepId = config.StepId,
                Content = $"Step validation failed: {errors}",
                Context = context
            };
            yield break;
        }

        yield return new PipelineStreamEvent
        {
            Type = PipelineStreamEventType.StepStarted,
            StepId = config.StepId,
            Context = context
        };

        PipelineStreamEvent? lastEvent = null;
        var success = true;
        string? errorMessage = null;

        await foreach (var evt in step.ExecuteStreamingAsync(context, config, cancellationToken))
        {
            lastEvent = evt;

            if (evt.Type == PipelineStreamEventType.Error)
            {
                success = false;
                errorMessage = evt.Content;
            }

            yield return evt;
        }

        stepTrace.Complete(success, errorMessage);

        // Emit completion if not already done
        if (lastEvent?.Type != PipelineStreamEventType.StepComplete &&
            lastEvent?.Type != PipelineStreamEventType.Error)
        {
            yield return new PipelineStreamEvent
            {
                Type = success ? PipelineStreamEventType.StepComplete : PipelineStreamEventType.Error,
                StepId = config.StepId,
                Content = errorMessage,
                Context = lastEvent?.Context ?? context
            };
        }
    }

    private async Task<StepExecutionResult> ExecuteWithRetryAsync(
        IPipelineStep step,
        PipelineContext context,
        StepConfiguration config,
        int timeoutMs,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastException = null;
        StepExecutionResult? lastResult = null;

        while (attempt <= retryCount)
        {
            attempt++;

            try
            {
                var result = timeoutMs > 0
                    ? await ExecuteWithTimeoutAsync(step, context, config, timeoutMs, cancellationToken)
                    : await step.ExecuteAsync(context, config, cancellationToken);

                if (result.Success)
                    return result;

                lastResult = result;
                _logger.LogDebug("Step {StepId} attempt {Attempt} failed: {Error}", config.StepId, attempt, result.ErrorMessage);
            }
            catch (TimeoutRejectedException ex)
            {
                lastException = ex;
                _logger.LogDebug("Step {StepId} attempt {Attempt} timed out", config.StepId, attempt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                _logger.LogDebug(ex, "Step {StepId} attempt {Attempt} threw exception", config.StepId, attempt);
            }

            if (attempt <= retryCount)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(1000 * attempt, 5000));
                _logger.LogDebug("Retrying step {StepId} in {Delay}ms", config.StepId, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        if (lastResult != null)
            return lastResult;

        return StepExecutionResult.Failed(
            context,
            lastException?.Message ?? "Step failed after all retry attempts");
    }

    private async Task<StepExecutionResult> ExecuteWithTimeoutAsync(
        IPipelineStep step,
        PipelineContext context,
        StepConfiguration config,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await step.ExecuteAsync(context, config, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutRejectedException($"Step {config.StepId} timed out after {timeoutMs}ms");
        }
    }

    private bool EvaluateCondition(PipelineContext context, string condition)
    {
        // Simple condition evaluation - supports variable checks
        // Format: "variable.name == value" or "variable.name != value" or "variable.name exists"
        try
        {
            condition = condition.Trim();

            if (condition.EndsWith(" exists", StringComparison.OrdinalIgnoreCase))
            {
                var varName = condition.Substring(0, condition.Length - 7).Trim();
                return context.HasVariable(varName);
            }

            if (condition.Contains("=="))
            {
                var parts = condition.Split("==", 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    var varValue = context.GetVariable<string>(parts[0]);
                    return string.Equals(varValue, parts[1].Trim('"', '\''), StringComparison.OrdinalIgnoreCase);
                }
            }

            if (condition.Contains("!="))
            {
                var parts = condition.Split("!=", 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    var varValue = context.GetVariable<string>(parts[0]);
                    return !string.Equals(varValue, parts[1].Trim('"', '\''), StringComparison.OrdinalIgnoreCase);
                }
            }

            // Default: check if variable is truthy
            var val = context.GetVariable<object>(condition);
            return val != null && !val.Equals(false) && !val.Equals(0) && !val.Equals("");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate condition: {Condition}", condition);
            return false;
        }
    }
}

/// <summary>
/// Options for the step executor.
/// </summary>
public sealed class StepExecutorOptions
{
    /// <summary>
    /// Default timeout in milliseconds for steps without explicit timeout.
    /// 0 = no timeout.
    /// </summary>
    public int DefaultTimeoutMs { get; init; } = 60000; // 1 minute

    /// <summary>
    /// Default retry count for steps without explicit retry configuration.
    /// </summary>
    public int DefaultRetryCount { get; init; } = 0;
}
