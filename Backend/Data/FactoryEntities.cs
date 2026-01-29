// ============================================================================
// FACTORY DATABASE ENTITIES
// Maps to existing factory tables: project_intents, feature_graphs, 
// generated_projects, llm_calls, build_repairs, code_knowledge, feature_templates
// ============================================================================

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Data;

/// <summary>
/// Maps to project_intents table - stores user prompts for project creation
/// </summary>
[Table("project_intents")]
public class ProjectIntentEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    public string? UserId { get; set; }

    [Required]
    [Column("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [Required]
    [Column("normalized_prompt")]
    public string NormalizedPrompt { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Maps to feature_graphs table - stores generated feature graphs
/// </summary>
[Table("feature_graphs")]
public class FeatureGraphEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("intent_id")]
    public string IntentId { get; set; } = string.Empty;

    [Required]
    [Column("project_name")]
    [MaxLength(128)]
    public string ProjectName { get; set; } = string.Empty;

    [Required]
    [Column("language")]
    [MaxLength(32)]
    public string Language { get; set; } = string.Empty;

    [Required]
    [Column("project_kind")]
    [MaxLength(32)]
    public string ProjectKind { get; set; } = string.Empty;

    [Required]
    [Column("graph_json")]
    public string GraphJson { get; set; } = string.Empty;

    [Column("is_valid")]
    public bool IsValid { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Maps to generated_projects table - stores generated project metadata
/// </summary>
[Table("generated_projects")]
public class GeneratedProjectEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("intent_id")]
    public string IntentId { get; set; } = string.Empty;

    [Required]
    [Column("project_name")]
    [MaxLength(128)]
    public string ProjectName { get; set; } = string.Empty;

    [Required]
    [Column("zip_hash")]
    [MaxLength(64)]
    public string ZipHash { get; set; } = string.Empty;

    [Column("file_count")]
    public int FileCount { get; set; }

    [Column("build_passed")]
    public bool BuildPassed { get; set; }

    [Column("build_log")]
    public string? BuildLog { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Maps to llm_calls table - caches LLM calls for reuse
/// </summary>
[Table("llm_calls")]
public class LlmCallEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("intent_id")]
    public string IntentId { get; set; } = string.Empty;

    [Required]
    [Column("pass_name")]
    [MaxLength(32)]
    public string PassName { get; set; } = string.Empty;

    [Required]
    [Column("prompt_hash")]
    [MaxLength(64)]
    public string PromptHash { get; set; } = string.Empty;

    [Required]
    [Column("input_prompt")]
    public string InputPrompt { get; set; } = string.Empty;

    [Column("output_text")]
    public string? OutputText { get; set; }

    [Column("is_valid")]
    public bool IsValid { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Maps to build_repairs table - stores build repair attempts
/// </summary>
[Table("build_repairs")]
public class BuildRepairEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [Column("attempt")]
    public int Attempt { get; set; }

    [Required]
    [Column("error_log")]
    public string ErrorLog { get; set; } = string.Empty;

    [Column("patch_diff")]
    public string? PatchDiff { get; set; }

    [Column("success")]
    public bool Success { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Maps to code_knowledge table - stores learned code patterns
/// </summary>
[Table("code_knowledge")]
public class CodeKnowledgeEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("language")]
    [MaxLength(32)]
    public string Language { get; set; } = string.Empty;

    [Required]
    [Column("pattern")]
    [MaxLength(64)]
    public string Pattern { get; set; } = string.Empty;

    [Required]
    [Column("issue_signature")]
    [MaxLength(255)]
    public string IssueSignature { get; set; } = string.Empty;

    [Required]
    [Column("fix_description")]
    public string FixDescription { get; set; } = string.Empty;

    [Column("confidence")]
    public double Confidence { get; set; } = 0.5;

    [Column("source_project_id")]
    public string? SourceProjectId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Maps to feature_templates table - stores reusable feature templates
/// </summary>
[Table("feature_templates")]
public class FeatureTemplateEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("language")]
    [MaxLength(32)]
    public string Language { get; set; } = string.Empty;

    [Required]
    [Column("project_kind")]
    [MaxLength(32)]
    public string ProjectKind { get; set; } = string.Empty;

    [Required]
    [Column("pattern")]
    [MaxLength(64)]
    public string Pattern { get; set; } = string.Empty;

    [Required]
    [Column("template_graph")]
    public string TemplateGraph { get; set; } = string.Empty;

    [Column("confidence")]
    public double Confidence { get; set; } = 0.7;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
