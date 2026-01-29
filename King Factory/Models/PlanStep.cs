namespace LittleHelperAI.KingFactory.Models;

/// <summary>
/// Represents a single step in an execution plan.
/// </summary>
public class PlanStep
{
    public int Order { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public Dictionary<string, object>? ToolArguments { get; set; }
    public PlanStepStatus Status { get; set; } = PlanStepStatus.Pending;
    public string? Result { get; set; }
    public string? Error { get; set; }
    public List<PlanStep>? SubSteps { get; set; }
}

public enum PlanStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Represents a complete execution plan.
/// </summary>
public class ExecutionPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Goal { get; set; } = string.Empty;
    public List<PlanStep> Steps { get; set; } = new();
    public PlanStatus Status { get; set; } = PlanStatus.Created;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public enum PlanStatus
{
    Created,
    Approved,
    Executing,
    Completed,
    Failed,
    Cancelled
}
