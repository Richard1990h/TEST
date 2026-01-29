namespace LittleHelperAI.KingFactory.Reasoning;

/// <summary>
/// Represents a single step in an execution plan.
/// </summary>
public class PlanStep
{
    /// <summary>
    /// Unique identifier for this step.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Step number in sequence.
    /// </summary>
    public int StepNumber { get; set; }

    /// <summary>
    /// Description of what this step does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Type of action for this step.
    /// </summary>
    public StepType Type { get; set; } = StepType.Action;

    /// <summary>
    /// Current status of the step.
    /// </summary>
    public StepStatus Status { get; set; } = StepStatus.Pending;

    /// <summary>
    /// Tool to use for this step (if applicable).
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Arguments for the tool (if applicable).
    /// </summary>
    public Dictionary<string, object>? ToolArguments { get; set; }

    /// <summary>
    /// Result of executing this step.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Error message if step failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Dependencies on other steps (by ID).
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Time when step started execution.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Time when step completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Whether this step can be retried.
    /// </summary>
    public bool CanRetry { get; set; } = true;

    /// <summary>
    /// Number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Check if step is ready to execute.
    /// </summary>
    public bool IsReady(IEnumerable<PlanStep> allSteps)
    {
        if (Status != StepStatus.Pending)
            return false;

        if (Dependencies.Count == 0)
            return true;

        return Dependencies.All(depId =>
            allSteps.FirstOrDefault(s => s.Id == depId)?.Status == StepStatus.Completed);
    }

    /// <summary>
    /// Mark step as started.
    /// </summary>
    public void Start()
    {
        Status = StepStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark step as completed.
    /// </summary>
    public void Complete(string? result = null)
    {
        Status = StepStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        Result = result;
    }

    /// <summary>
    /// Mark step as failed.
    /// </summary>
    public void Fail(string error)
    {
        Status = CanRetry && RetryCount < MaxRetries ? StepStatus.Pending : StepStatus.Failed;
        Error = error;
        RetryCount++;
    }

    /// <summary>
    /// Mark step as skipped.
    /// </summary>
    public void Skip(string reason)
    {
        Status = StepStatus.Skipped;
        Result = reason;
        CompletedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Types of plan steps.
/// </summary>
public enum StepType
{
    /// <summary>
    /// A concrete action to perform.
    /// </summary>
    Action,

    /// <summary>
    /// A decision point that may branch.
    /// </summary>
    Decision,

    /// <summary>
    /// Gathering information.
    /// </summary>
    Research,

    /// <summary>
    /// Validation or verification.
    /// </summary>
    Validation,

    /// <summary>
    /// User confirmation required.
    /// </summary>
    Confirmation,

    /// <summary>
    /// Waiting for external event.
    /// </summary>
    Wait
}

/// <summary>
/// Status of a plan step.
/// </summary>
public enum StepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    Blocked
}

/// <summary>
/// Represents a complete execution plan.
/// </summary>
public class ExecutionPlan
{
    /// <summary>
    /// Unique identifier for this plan.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Original task/goal this plan addresses.
    /// </summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>
    /// Steps in this plan.
    /// </summary>
    public List<PlanStep> Steps { get; set; } = new();

    /// <summary>
    /// Current status of the plan.
    /// </summary>
    public PlanStatus Status { get; set; } = PlanStatus.Created;

    /// <summary>
    /// When the plan was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the plan started execution.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the plan completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Get the next step ready for execution.
    /// </summary>
    public PlanStep? GetNextStep()
    {
        return Steps.FirstOrDefault(s => s.IsReady(Steps));
    }

    /// <summary>
    /// Check if all steps are complete.
    /// </summary>
    public bool IsComplete => Steps.All(s =>
        s.Status == StepStatus.Completed || s.Status == StepStatus.Skipped);

    /// <summary>
    /// Check if plan has failed.
    /// </summary>
    public bool HasFailed => Steps.Any(s => s.Status == StepStatus.Failed);

    /// <summary>
    /// Get completion percentage.
    /// </summary>
    public double CompletionPercentage
    {
        get
        {
            if (Steps.Count == 0) return 0;
            var completed = Steps.Count(s => s.Status == StepStatus.Completed || s.Status == StepStatus.Skipped);
            return (double)completed / Steps.Count * 100;
        }
    }
}

/// <summary>
/// Status of an execution plan.
/// </summary>
public enum PlanStatus
{
    Created,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}
