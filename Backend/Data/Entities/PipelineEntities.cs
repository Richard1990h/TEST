using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Data.Entities;

/// <summary>
/// V2 Pipeline definition stored in database.
/// </summary>
[Table("pipelines_v2")]
public class PipelineV2Entity
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("name")]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    [MaxLength(1000)]
    public string? Description { get; set; }

    [Column("version")]
    [MaxLength(20)]
    public string Version { get; set; } = "1.0.0";

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "Draft";

    [Column("is_primary")]
    public bool IsPrimary { get; set; }

    [Required]
    [Column("config_json")]
    public string ConfigJson { get; set; } = "{}";

    [Column("tags")]
    [MaxLength(500)]
    public string? Tags { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("created_by")]
    public int? CreatedBy { get; set; }
}

/// <summary>
/// Pipeline version history.
/// </summary>
[Table("pipeline_versions")]
public class PipelineVersionEntity
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("pipeline_id")]
    [MaxLength(36)]
    public string PipelineId { get; set; } = string.Empty;

    [Required]
    [Column("version")]
    [MaxLength(20)]
    public string Version { get; set; } = "1.0.0";

    [Required]
    [Column("config_json")]
    public string ConfigJson { get; set; } = "{}";

    [Column("commit_message")]
    [MaxLength(500)]
    public string? CommitMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("created_by")]
    public int? CreatedBy { get; set; }
}

/// <summary>
/// Pipeline execution record.
/// </summary>
[Table("pipeline_executions")]
public class PipelineExecutionEntity
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("pipeline_id")]
    [MaxLength(36)]
    public string PipelineId { get; set; } = string.Empty;

    [Column("pipeline_version")]
    [MaxLength(20)]
    public string? PipelineVersion { get; set; }

    [Column("conversation_id")]
    [MaxLength(36)]
    public string? ConversationId { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Required]
    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "Running";

    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("duration_ms")]
    public long? DurationMs { get; set; }

    [Column("step_count")]
    public int StepCount { get; set; }

    [Column("completed_step_count")]
    public int CompletedStepCount { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("input_summary")]
    [MaxLength(500)]
    public string? InputSummary { get; set; }

    [Column("output_summary")]
    [MaxLength(500)]
    public string? OutputSummary { get; set; }
}

/// <summary>
/// Individual step execution log.
/// </summary>
[Table("pipeline_step_logs")]
public class PipelineStepLogEntity
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("execution_id")]
    [MaxLength(36)]
    public string ExecutionId { get; set; } = string.Empty;

    [Required]
    [Column("step_id")]
    [MaxLength(100)]
    public string StepId { get; set; } = string.Empty;

    [Required]
    [Column("step_type")]
    [MaxLength(100)]
    public string StepType { get; set; } = string.Empty;

    [Column("step_order")]
    public int StepOrder { get; set; }

    [Required]
    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "Running";

    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("duration_ms")]
    public long? DurationMs { get; set; }

    [Column("input_json")]
    public string? InputJson { get; set; }

    [Column("output_json")]
    public string? OutputJson { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Aggregated pipeline metrics by day.
/// </summary>
[Table("pipeline_metrics")]
public class PipelineMetricEntity
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("pipeline_id")]
    [MaxLength(36)]
    public string PipelineId { get; set; } = string.Empty;

    [Required]
    [Column("date")]
    public DateTime Date { get; set; }

    [Column("total_executions")]
    public int TotalExecutions { get; set; }

    [Column("success_count")]
    public int SuccessCount { get; set; }

    [Column("failure_count")]
    public int FailureCount { get; set; }

    [Column("avg_duration_ms")]
    public long? AvgDurationMs { get; set; }

    [Column("min_duration_ms")]
    public long? MinDurationMs { get; set; }

    [Column("max_duration_ms")]
    public long? MaxDurationMs { get; set; }

    [Column("total_steps_executed")]
    public int TotalStepsExecuted { get; set; }

    [Column("total_tool_calls")]
    public int TotalToolCalls { get; set; }
}
