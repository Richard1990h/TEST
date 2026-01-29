using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Intent;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Prompts;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LittleHelperAI.KingFactory.Reasoning;

/// <summary>
/// Decomposes complex tasks into executable steps.
/// </summary>
public interface ITaskDecomposer
{
    /// <summary>
    /// Decompose a task into an execution plan.
    /// </summary>
    Task<ExecutionPlan> DecomposeAsync(string task, IntentResult intent, Func<string, Task>? onToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refine a plan based on execution results.
    /// </summary>
    Task<ExecutionPlan> RefineAsync(ExecutionPlan plan, string feedback, Func<string, Task>? onToken = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// LLM-based task decomposition.
/// </summary>
public class TaskDecomposer : ITaskDecomposer
{
    private readonly ILogger<TaskDecomposer> _logger;
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly ISystemPrompts _systemPrompts;

    public TaskDecomposer(
        ILogger<TaskDecomposer> logger,
        IUnifiedLlmProvider llmProvider,
        ISystemPrompts systemPrompts)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _systemPrompts = systemPrompts;
    }

    public async Task<ExecutionPlan> DecomposeAsync(string task, IntentResult intent, Func<string, Task>? onToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Decomposing task: {Task}", task.Substring(0, Math.Min(100, task.Length)));

        var plan = new ExecutionPlan { Goal = task };

        // For simple intents, create a single-step plan
        if (IsSimpleIntent(intent))
        {
            plan.Steps.Add(CreateSimpleStep(task, intent));
            return plan;
        }

        // For complex tasks, use LLM to decompose
        var prompt = BuildDecompositionPrompt(task, intent);
        var response = await GenerateResponseAsync(prompt, onToken, cancellationToken);

        // Parse the LLM response into steps
        var steps = ParseSteps(response);

        if (steps.Count == 0)
        {
            // Fallback to single step if parsing fails
            _logger.LogWarning("Failed to parse steps, using single-step fallback");
            plan.Steps.Add(new PlanStep
            {
                StepNumber = 1,
                Description = task,
                Type = StepType.Action
            });
        }
        else
        {
            plan.Steps = steps;
        }

        // Set up dependencies
        SetupDependencies(plan);

        _logger.LogInformation("Created plan with {StepCount} steps", plan.Steps.Count);

        return plan;
    }

    public async Task<ExecutionPlan> RefineAsync(ExecutionPlan plan, string feedback, Func<string, Task>? onToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refining plan based on feedback");

        var prompt = BuildRefinementPrompt(plan, feedback);
        var response = await GenerateResponseAsync(prompt, onToken, cancellationToken);

        // Parse refined steps
        var newSteps = ParseSteps(response);

        if (newSteps.Count > 0)
        {
            // Preserve completed steps, replace pending ones
            var completedSteps = plan.Steps.Where(s => s.Status == StepStatus.Completed).ToList();
            var newPendingSteps = newSteps.Skip(completedSteps.Count).ToList();

            // Renumber new steps
            for (var i = 0; i < newPendingSteps.Count; i++)
            {
                newPendingSteps[i].StepNumber = completedSteps.Count + i + 1;
            }

            plan.Steps = completedSteps.Concat(newPendingSteps).ToList();
            SetupDependencies(plan);
        }

        return plan;
    }

    private bool IsSimpleIntent(IntentResult intent)
    {
        // Simple intents that don't need decomposition
        return intent.Intent switch
        {
            IntentType.Help => true,
            IntentType.Confirmation => true,
            IntentType.Rejection => true,
            IntentType.Clarification => true,
            IntentType.GeneralQuery => intent.Confidence < 0.5,
            _ => false
        };
    }

    private PlanStep CreateSimpleStep(string task, IntentResult intent)
    {
        var step = new PlanStep
        {
            StepNumber = 1,
            Description = task,
            Type = intent.Category == IntentCategory.Information ? StepType.Research : StepType.Action
        };

        // Map intent to tool
        step.ToolName = intent.Intent switch
        {
            IntentType.CodeRead or IntentType.FileList => "read_file",
            IntentType.FileCreate or IntentType.CodeWrite => "write_file",
            IntentType.ShellCommand => "run_command",
            IntentType.Search => "list_files",
            _ => null
        };

        return step;
    }

    private string BuildDecompositionPrompt(string task, IntentResult intent)
    {
        return $@"{_systemPrompts.GetPlanningPrompt()}

## Task to Decompose
{task}

## Detected Intent
- Type: {intent.Intent}
- Category: {intent.Category}
- Confidence: {intent.Confidence:P0}

## Instructions
Break this task into specific, actionable steps. For each step, provide:
1. Step number
2. Description of what to do
3. Type: action, research, validation, or confirmation
4. Tool to use (if applicable): read_file, write_file, list_files, run_command, fetch

Format each step as:
STEP [number]: [description]
TYPE: [action/research/validation/confirmation]
TOOL: [tool_name or 'none']

Provide 2-10 steps as appropriate for the task complexity.
";
    }

    private string BuildRefinementPrompt(ExecutionPlan plan, string feedback)
    {
        var completedSteps = string.Join("\n", plan.Steps
            .Where(s => s.Status == StepStatus.Completed)
            .Select(s => $"- Step {s.StepNumber}: {s.Description} [DONE]"));

        var pendingSteps = string.Join("\n", plan.Steps
            .Where(s => s.Status != StepStatus.Completed)
            .Select(s => $"- Step {s.StepNumber}: {s.Description}"));

        return $@"{_systemPrompts.GetPlanningPrompt()}

## Original Goal
{plan.Goal}

## Completed Steps
{completedSteps}

## Pending Steps
{pendingSteps}

## Feedback
{feedback}

## Instructions
Refine the remaining steps based on the feedback. Keep the same format:
STEP [number]: [description]
TYPE: [action/research/validation/confirmation]
TOOL: [tool_name or 'none']
";
    }

    private async Task<string> GenerateResponseAsync(string prompt, Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        var response = new System.Text.StringBuilder();

        await foreach (var token in _llmProvider.StreamAsync(prompt, maxTokens: 1000, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            response.Append(token);
            if (onToken != null)
            {
                await onToken(token);
            }
        }

        return response.ToString();
    }

    private List<PlanStep> ParseSteps(string response)
    {
        var steps = new List<PlanStep>();
        var stepPattern = new Regex(
            @"STEP\s*(\d+):\s*(.+?)(?:\r?\n|\r)TYPE:\s*(\w+)(?:\r?\n|\r)TOOL:\s*(\w+|none)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var matches = stepPattern.Matches(response);

        foreach (Match match in matches)
        {
            var step = new PlanStep
            {
                StepNumber = int.Parse(match.Groups[1].Value),
                Description = match.Groups[2].Value.Trim(),
                Type = ParseStepType(match.Groups[3].Value),
                ToolName = match.Groups[4].Value.ToLowerInvariant() == "none"
                    ? null
                    : match.Groups[4].Value.ToLowerInvariant()
            };

            steps.Add(step);
        }

        // Fallback: Try simpler parsing if regex failed
        if (steps.Count == 0)
        {
            steps = ParseSimpleSteps(response);
        }

        return steps;
    }

    private List<PlanStep> ParseSimpleSteps(string response)
    {
        var steps = new List<PlanStep>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var stepNumber = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Match numbered list items
            var numberedMatch = Regex.Match(trimmed, @"^(\d+)[.)\]]\s*(.+)$");
            if (numberedMatch.Success)
            {
                stepNumber++;
                steps.Add(new PlanStep
                {
                    StepNumber = stepNumber,
                    Description = numberedMatch.Groups[2].Value.Trim(),
                    Type = StepType.Action
                });
            }
            // Match bullet points
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                stepNumber++;
                steps.Add(new PlanStep
                {
                    StepNumber = stepNumber,
                    Description = trimmed.Substring(2).Trim(),
                    Type = StepType.Action
                });
            }
        }

        return steps;
    }

    private StepType ParseStepType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "action" => StepType.Action,
            "research" => StepType.Research,
            "validation" => StepType.Validation,
            "confirmation" => StepType.Confirmation,
            "decision" => StepType.Decision,
            "wait" => StepType.Wait,
            _ => StepType.Action
        };
    }

    private void SetupDependencies(ExecutionPlan plan)
    {
        // Simple sequential dependencies by default
        for (var i = 1; i < plan.Steps.Count; i++)
        {
            plan.Steps[i].Dependencies.Add(plan.Steps[i - 1].Id);
        }
    }
}
