using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Intent;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Tools;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace LittleHelperAI.KingFactory.Reasoning;

/// <summary>
/// Executes reasoning loops for complex tasks.
/// </summary>
public interface IReasoningLoop
{
    /// <summary>
    /// Execute a task with reasoning.
    /// </summary>
    IAsyncEnumerable<ReasoningOutput> ExecuteAsync(string task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute with an existing plan.
    /// </summary>
    IAsyncEnumerable<ReasoningOutput> ExecutePlanAsync(ExecutionPlan plan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Output from the reasoning loop.
/// </summary>
public class ReasoningOutput
{
    /// <summary>
    /// Type of output.
    /// </summary>
    public ReasoningOutputType Type { get; set; }

    /// <summary>
    /// Content of the output.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Current step being executed (if applicable).
    /// </summary>
    public PlanStep? CurrentStep { get; set; }

    /// <summary>
    /// Overall progress percentage.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Whether this is a final output.
    /// </summary>
    public bool IsFinal { get; set; }
}

/// <summary>
/// Types of reasoning output.
/// </summary>
public enum ReasoningOutputType
{
    Thinking,
    Planning,
    Executing,
    ToolCall,
    ToolResult,
    Progress,
    Response,
    Error,
    Complete
}

/// <summary>
/// Main reasoning loop implementation.
/// </summary>
public class ReasoningLoop : IReasoningLoop
{
    private readonly ILogger<ReasoningLoop> _logger;
    private readonly IIntentClassifier _intentClassifier;
    private readonly ITaskDecomposer _taskDecomposer;
    private readonly IToolRouter _toolRouter;
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly int _maxIterations;

    public ReasoningLoop(
        ILogger<ReasoningLoop> logger,
        IIntentClassifier intentClassifier,
        ITaskDecomposer taskDecomposer,
        IToolRouter toolRouter,
        IUnifiedLlmProvider llmProvider,
        int maxIterations = 20)
    {
        _logger = logger;
        _intentClassifier = intentClassifier;
        _taskDecomposer = taskDecomposer;
        _toolRouter = toolRouter;
        _llmProvider = llmProvider;
        _maxIterations = maxIterations;
    }

    public async IAsyncEnumerable<ReasoningOutput> ExecuteAsync(
        string task,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting reasoning loop for task");

        // Classify intent
        yield return new ReasoningOutput
        {
            Type = ReasoningOutputType.Thinking,
            Content = "Analyzing your request..."
        };

        var intent = await _intentClassifier.ClassifyAsync(task, cancellationToken);

        _logger.LogDebug("Intent classified: {Intent} ({Confidence})", intent.Intent, intent.Confidence);

        // Create execution plan
        yield return new ReasoningOutput
        {
            Type = ReasoningOutputType.Planning,
            Content = "Creating execution plan..."
        };

        var planningTokens = Channel.CreateUnbounded<string>();
        var planTask = Task.Run(async () =>
        {
            try
            {
                return await _taskDecomposer.DecomposeAsync(
                    task,
                    intent,
                    token =>
                    {
                        planningTokens.Writer.TryWrite(token);
                        return Task.CompletedTask;
                    },
                    cancellationToken);
            }
            finally
            {
                planningTokens.Writer.TryComplete();
            }
        }, cancellationToken);

        await foreach (var token in planningTokens.Reader.ReadAllAsync(cancellationToken))
        {
            yield return new ReasoningOutput
            {
                Type = ReasoningOutputType.Planning,
                Content = token
            };
        }

        var plan = await planTask;

        _logger.LogInformation("Created plan with {StepCount} steps", plan.Steps.Count);

        yield return new ReasoningOutput
        {
            Type = ReasoningOutputType.Planning,
            Content = FormatPlan(plan)
        };

        // Execute the plan
        await foreach (var output in ExecutePlanAsync(plan, cancellationToken))
        {
            yield return output;
        }
    }

    public async IAsyncEnumerable<ReasoningOutput> ExecutePlanAsync(
        ExecutionPlan plan,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        plan.Status = PlanStatus.Running;
        plan.StartedAt = DateTime.UtcNow;

        var iteration = 0;
        var context = new List<StepResult>();

        while (!plan.IsComplete && !plan.HasFailed && iteration < _maxIterations)
        {
            iteration++;
            cancellationToken.ThrowIfCancellationRequested();

            var step = plan.GetNextStep();
            if (step == null)
            {
                _logger.LogWarning("No ready steps found but plan not complete");
                break;
            }

            _logger.LogInformation("Executing step {StepNumber}: {Description}",
                step.StepNumber, step.Description);

            yield return new ReasoningOutput
            {
                Type = ReasoningOutputType.Executing,
                Content = $"Step {step.StepNumber}: {step.Description}",
                CurrentStep = step,
                Progress = plan.CompletionPercentage
            };

            step.Start();

            // Handle different step types
            StepResult result;
            var streamedReasoning = false;
            if (step.Type == StepType.Confirmation)
            {
                result = await HandleConfirmationStep(step, cancellationToken);
            }
            else if ((step.Type == StepType.Action || step.Type == StepType.Research) && step.ToolName != null)
            {
                result = await ExecuteToolStep(step, context, cancellationToken);
            }
            else
            {
                var reasoningTokens = Channel.CreateUnbounded<string>();
                var stepTask = Task.Run(async () =>
                {
                    try
                    {
                        return await ExecuteReasoningStep(
                            step,
                            context,
                            token =>
                            {
                                reasoningTokens.Writer.TryWrite(token);
                                return Task.CompletedTask;
                            },
                            cancellationToken);
                    }
                    finally
                    {
                        reasoningTokens.Writer.TryComplete();
                    }
                }, cancellationToken);

                await foreach (var token in reasoningTokens.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return new ReasoningOutput
                    {
                        Type = ReasoningOutputType.Response,
                        Content = token,
                        CurrentStep = step,
                        Progress = plan.CompletionPercentage
                    };
                }

                result = await stepTask;
                streamedReasoning = true;
            }

            context.Add(result);

            if (result.Success)
            {
                step.Complete(result.Output);

                yield return new ReasoningOutput
                {
                    Type = step.ToolName != null ? ReasoningOutputType.ToolResult : ReasoningOutputType.Progress,
                    Content = streamedReasoning ? "Step completed" : (result.Output ?? "Step completed"),
                    CurrentStep = step,
                    Progress = plan.CompletionPercentage
                };
            }
            else
            {
                step.Fail(result.Error ?? "Unknown error");

                yield return new ReasoningOutput
                {
                    Type = ReasoningOutputType.Error,
                    Content = $"Step failed: {result.Error}",
                    CurrentStep = step,
                    Progress = plan.CompletionPercentage
                };

                // Try to recover if step can be retried
                if (step.Status == StepStatus.Pending && step.RetryCount < step.MaxRetries)
                {
                    _logger.LogInformation("Retrying step {StepNumber}", step.StepNumber);
                }
            }
        }

        // Final output
        if (plan.IsComplete)
        {
            plan.Status = PlanStatus.Completed;
            plan.CompletedAt = DateTime.UtcNow;

            yield return new ReasoningOutput
            {
                Type = ReasoningOutputType.Complete,
                Content = GenerateSummary(plan, context),
                Progress = 100,
                IsFinal = true
            };
        }
        else if (plan.HasFailed)
        {
            plan.Status = PlanStatus.Failed;

            yield return new ReasoningOutput
            {
                Type = ReasoningOutputType.Error,
                Content = "The task could not be completed. Please try rephrasing your request.",
                Progress = plan.CompletionPercentage,
                IsFinal = true
            };
        }
        else
        {
            yield return new ReasoningOutput
            {
                Type = ReasoningOutputType.Error,
                Content = "Maximum iterations reached. The task may be too complex.",
                Progress = plan.CompletionPercentage,
                IsFinal = true
            };
        }
    }

    private async Task<StepResult> ExecuteToolStep(PlanStep step, List<StepResult> context, CancellationToken cancellationToken)
    {
        try
        {
            var toolCall = new ToolCall
            {
                ToolName = step.ToolName!,
                Arguments = step.ToolArguments ?? new Dictionary<string, object>()
            };

            // If no arguments provided, try to infer from step description
            if (toolCall.Arguments.Count == 0)
            {
                toolCall.Arguments = InferToolArguments(step);
            }

            var result = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);

            return new StepResult
            {
                StepId = step.Id,
                Success = result.Success,
                Output = result.Output,
                Error = result.Error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed for step {StepNumber}", step.StepNumber);
            return new StepResult
            {
                StepId = step.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<StepResult> ExecuteReasoningStep(
        PlanStep step,
        List<StepResult> context,
        Func<string, Task>? onToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildStepPrompt(step, context);
            var response = new System.Text.StringBuilder();

            await foreach (var token in _llmProvider.StreamAsync(prompt, maxTokens: 500, cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                response.Append(token);
                if (onToken != null)
                {
                    await onToken(token);
                }
            }

            return new StepResult
            {
                StepId = step.Id,
                Success = true,
                Output = response.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reasoning step failed for step {StepNumber}", step.StepNumber);
            return new StepResult
            {
                StepId = step.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private Task<StepResult> HandleConfirmationStep(PlanStep step, CancellationToken cancellationToken)
    {
        // For now, auto-confirm. In production, this would wait for user input
        return Task.FromResult(new StepResult
        {
            StepId = step.Id,
            Success = true,
            Output = "Confirmed (auto)"
        });
    }

    private Dictionary<string, object> InferToolArguments(PlanStep step)
    {
        var args = new Dictionary<string, object>();
        var description = step.Description.ToLowerInvariant();

        // Extract paths from description
        var pathMatch = System.Text.RegularExpressions.Regex.Match(
            step.Description,
            @"[\w./\\]+\.\w+");

        if (pathMatch.Success)
        {
            args["path"] = pathMatch.Value;
        }

        // Extract commands
        if (step.ToolName == "run_command")
        {
            var commandMatch = System.Text.RegularExpressions.Regex.Match(
                step.Description,
                @"`([^`]+)`|'([^']+)'");

            if (commandMatch.Success)
            {
                args["command"] = commandMatch.Groups[1].Success
                    ? commandMatch.Groups[1].Value
                    : commandMatch.Groups[2].Value;
            }
        }

        return args;
    }

    private string BuildStepPrompt(PlanStep step, List<StepResult> context)
    {
        var contextStr = context.Count > 0
            ? "Previous results:\n" + string.Join("\n", context.TakeLast(3).Select(c => $"- {c.Output?.Substring(0, Math.Min(200, c.Output?.Length ?? 0))}"))
            : "";

        return $@"You are executing step {step.StepNumber} of a plan.

Step: {step.Description}
Type: {step.Type}

{contextStr}

Complete this step and provide a concise result.";
    }

    private string FormatPlan(ExecutionPlan plan)
    {
        var lines = new List<string>
        {
            $"**Plan for:** {plan.Goal}",
            ""
        };

        foreach (var step in plan.Steps)
        {
            var tool = step.ToolName != null ? $" [{step.ToolName}]" : "";
            lines.Add($"{step.StepNumber}. {step.Description}{tool}");
        }

        return string.Join("\n", lines);
    }

    private string GenerateSummary(ExecutionPlan plan, List<StepResult> results)
    {
        var successful = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        var lines = new List<string>
        {
            "**Task Completed**",
            "",
            $"- Steps completed: {successful}",
        };

        if (failed > 0)
        {
            lines.Add($"- Steps failed: {failed}");
        }

        // Add key results
        var keyResults = results.Where(r => r.Success && !string.IsNullOrEmpty(r.Output))
            .TakeLast(3)
            .Select(r => r.Output!.Length > 200 ? r.Output.Substring(0, 200) + "..." : r.Output);

        if (keyResults.Any())
        {
            lines.Add("");
            lines.Add("**Results:**");
            lines.AddRange(keyResults.Select(r => $"- {r}"));
        }

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Result of executing a step.
/// </summary>
internal class StepResult
{
    public string StepId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}
