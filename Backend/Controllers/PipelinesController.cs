using LittleHelperAI.KingFactory.Pipeline;
using Microsoft.AspNetCore.Mvc;

namespace LittleHelperAI.Backend.Controllers;

/// <summary>
/// API controller for managing pipelines.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PipelinesController : ControllerBase
{
    private readonly IPipelineRegistry _pipelineRegistry;
    private readonly ILogger<PipelinesController> _logger;

    public PipelinesController(IPipelineRegistry pipelineRegistry, ILogger<PipelinesController> logger)
    {
        _pipelineRegistry = pipelineRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Get all pipelines.
    /// </summary>
    [HttpGet]
    public ActionResult<PipelinesResponse> GetPipelines()
    {
        var pipelines = _pipelineRegistry.GetAll();
        var activePipeline = _pipelineRegistry.GetActivePipeline();

        return Ok(new PipelinesResponse
        {
            Pipelines = pipelines.Select(p => MapToDto(p)).ToList(),
            ActivePipelineId = activePipeline.Id,
            PrimaryPipelineId = _pipelineRegistry.PrimaryPipelineId
        });
    }

    /// <summary>
    /// Set the primary pipeline.
    /// </summary>
    [HttpPost("primary/{id}")]
    public ActionResult SetPrimaryPipeline(string id)
    {
        try
        {
            if (_pipelineRegistry is PipelineRegistry registry)
            {
                registry.SetPrimaryPipeline(id);
                return Ok(new { message = $"Primary pipeline set to '{id}'" });
            }
            return BadRequest(new { error = "Cannot set primary pipeline" });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get the primary pipeline.
    /// </summary>
    [HttpGet("primary")]
    public ActionResult<PipelineDto> GetPrimaryPipeline()
    {
        var pipeline = _pipelineRegistry.GetById(_pipelineRegistry.PrimaryPipelineId);
        if (pipeline == null)
        {
            return NotFound(new { error = "Primary pipeline not found" });
        }
        return Ok(MapToDto(pipeline));
    }

    /// <summary>
    /// Update keywords for a pipeline.
    /// </summary>
    [HttpPut("{id}/keywords")]
    public ActionResult UpdateKeywords(string id, [FromBody] List<string> keywords)
    {
        var pipeline = _pipelineRegistry.GetById(id);
        if (pipeline == null)
        {
            return NotFound(new { error = $"Pipeline '{id}' not found" });
        }

        pipeline.Config.TriggerKeywords = keywords ?? new List<string>();
        pipeline.UpdatedAt = DateTime.UtcNow;
        _pipelineRegistry.Save(pipeline);

        return Ok(new { message = "Keywords updated", keywords = pipeline.Config.TriggerKeywords });
    }

    /// <summary>
    /// Get which pipeline would be used for a given message.
    /// </summary>
    [HttpPost("match")]
    public ActionResult<PipelineDto> MatchPipeline([FromBody] string message)
    {
        var pipeline = _pipelineRegistry.GetPipelineForMessage(message);
        return Ok(MapToDto(pipeline));
    }

    /// <summary>
    /// Get a specific pipeline by ID.
    /// </summary>
    [HttpGet("{id}")]
    public ActionResult<PipelineDto> GetPipeline(string id)
    {
        var pipeline = _pipelineRegistry.GetById(id);
        if (pipeline == null)
        {
            return NotFound(new { error = $"Pipeline '{id}' not found" });
        }

        return Ok(MapToDto(pipeline));
    }

    /// <summary>
    /// Get the active pipeline.
    /// </summary>
    [HttpGet("active")]
    public ActionResult<PipelineDto> GetActivePipeline()
    {
        var pipeline = _pipelineRegistry.GetActivePipeline();
        return Ok(MapToDto(pipeline));
    }

    /// <summary>
    /// Set the active pipeline.
    /// </summary>
    [HttpPost("active/{id}")]
    public ActionResult SetActivePipeline(string id)
    {
        try
        {
            _pipelineRegistry.SetActivePipeline(id);
            return Ok(new { message = $"Active pipeline set to '{id}'" });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create or update a pipeline.
    /// </summary>
    [HttpPost]
    public ActionResult<PipelineDto> SavePipeline([FromBody] PipelineDto dto)
    {
        var pipeline = MapFromDto(dto);
        _pipelineRegistry.Save(pipeline);

        return Ok(MapToDto(pipeline));
    }

    /// <summary>
    /// Update an existing pipeline.
    /// </summary>
    [HttpPut("{id}")]
    public ActionResult<PipelineDto> UpdatePipeline(string id, [FromBody] PipelineDto dto)
    {
        var existing = _pipelineRegistry.GetById(id);
        if (existing == null)
        {
            return NotFound(new { error = $"Pipeline '{id}' not found" });
        }

        dto.Id = id; // Ensure ID matches
        var pipeline = MapFromDto(dto);
        _pipelineRegistry.Save(pipeline);

        return Ok(MapToDto(pipeline));
    }

    /// <summary>
    /// Delete a pipeline.
    /// </summary>
    [HttpDelete("{id}")]
    public ActionResult DeletePipeline(string id)
    {
        if (!_pipelineRegistry.Delete(id))
        {
            return BadRequest(new { error = $"Cannot delete pipeline '{id}'. It may be protected or not exist." });
        }

        return Ok(new { message = $"Pipeline '{id}' deleted" });
    }

    /// <summary>
    /// Get the step registry (available step types).
    /// </summary>
    [HttpGet("registry/steps")]
    public ActionResult<List<StepRegistryItemDto>> GetStepRegistry()
    {
        // Return all available step types
        var steps = new List<StepRegistryItemDto>
        {
            // Injection steps
            new() { TypeId = PipelineStepTypes.InjectConversation, Category = "Injection", Description = "Injects windowed conversation history", Version = "1.0.0", Inputs = new() { new("maxTokens", false) }, Outputs = new() { "conversationContext" } },
            new() { TypeId = PipelineStepTypes.InjectSystemPrompt, Category = "Injection", Description = "Injects the system prompt", Version = "1.0.0", Inputs = new(), Outputs = new() { "systemPrompt" } },
            new() { TypeId = PipelineStepTypes.InjectCorePrompt, Category = "Injection", Description = "Injects the core AI identity prompt", Version = "1.0.0", Inputs = new(), Outputs = new() { "corePrompt" } },
            new() { TypeId = PipelineStepTypes.InjectToolsPrompt, Category = "Injection", Description = "Injects available tools and usage instructions", Version = "1.0.0", Inputs = new(), Outputs = new() { "toolsPrompt" } },
            new() { TypeId = PipelineStepTypes.InjectPlanningPrompt, Category = "Injection", Description = "Injects planning mode prompt", Version = "1.0.0", Inputs = new(), Outputs = new() { "planningPrompt" } },
            new() { TypeId = PipelineStepTypes.InjectValidationPrompt, Category = "Injection", Description = "Injects validation prompt", Version = "1.0.0", Inputs = new(), Outputs = new() { "validationPrompt" } },
            new() { TypeId = PipelineStepTypes.InjectProjectContext, Category = "Injection", Description = "Injects current project context and file structure", Version = "1.0.0", Inputs = new() { new("projectPath", false) }, Outputs = new() { "projectContext" } },

            // LLM steps
            new() { TypeId = PipelineStepTypes.LlmGenerate, Category = "LLM", Description = "Generate a complete response from the LLM", Version = "1.0.0", Inputs = new() { new("prompt", true), new("maxTokens", false), new("temperature", false) }, Outputs = new() { "response" } },
            new() { TypeId = PipelineStepTypes.LlmStream, Category = "LLM", Description = "Stream a response from the LLM token by token", Version = "1.0.0", Inputs = new() { new("prompt", true), new("maxTokens", false), new("temperature", false) }, Outputs = new() { "response", "tokens" } },

            // Tool steps
            new() { TypeId = PipelineStepTypes.ToolParse, Category = "Tools", Description = "Parse tool calls from LLM response", Version = "1.0.0", Inputs = new() { new("response", true) }, Outputs = new() { "toolCalls" } },
            new() { TypeId = PipelineStepTypes.ToolExecute, Category = "Tools", Description = "Execute a single tool call", Version = "1.0.0", Inputs = new() { new("toolCall", true) }, Outputs = new() { "result", "success" } },
            new() { TypeId = PipelineStepTypes.ToolLoop, Category = "Tools", Description = "Parse and execute tools in a loop until complete", Version = "1.0.0", Inputs = new() { new("response", true), new("maxIterations", false) }, Outputs = new() { "finalResponse", "toolResults" } },

            // Analysis steps
            new() { TypeId = PipelineStepTypes.AnalyzeIntent, Category = "Analysis", Description = "Classify user intent", Version = "1.0.0", Inputs = new() { new("message", true) }, Outputs = new() { "intent", "confidence", "category" } },
            new() { TypeId = PipelineStepTypes.AnalyzeScope, Category = "Analysis", Description = "Extract scope from user request", Version = "1.0.0", Inputs = new() { new("message", true) }, Outputs = new() { "scope", "files", "technologies" } },

            // Validation steps
            new() { TypeId = PipelineStepTypes.ValidateOutput, Category = "Validation", Description = "Validate the generated output", Version = "1.0.0", Inputs = new() { new("response", true), new("originalQuery", true) }, Outputs = new() { "isValid", "issues" } },
            new() { TypeId = PipelineStepTypes.ValidateCode, Category = "Validation", Description = "Validate generated code", Version = "1.0.0", Inputs = new() { new("code", true), new("language", false) }, Outputs = new() { "isValid", "errors", "warnings" } },

            // Transform steps
            new() { TypeId = PipelineStepTypes.TransformWriteFile, Category = "Transform", Description = "Write content to a file", Version = "1.0.0", Inputs = new() { new("path", true), new("content", true) }, Outputs = new() { "success" } },
            new() { TypeId = PipelineStepTypes.TransformApplyPatch, Category = "Transform", Description = "Apply a code patch to a file", Version = "1.0.0", Inputs = new() { new("path", true), new("patch", true) }, Outputs = new() { "success", "modifiedContent" } },

            // Build steps
            new() { TypeId = PipelineStepTypes.BuildDetect, Category = "Build", Description = "Detect the build system used by a project", Version = "1.0.0", Inputs = new() { new("projectPath", true) }, Outputs = new() { "buildSystem", "projectType" } },
            new() { TypeId = PipelineStepTypes.BuildRestore, Category = "Build", Description = "Restore project dependencies", Version = "1.0.0", Inputs = new() { new("projectPath", true), new("buildSystem", true) }, Outputs = new() { "success" } },
            new() { TypeId = PipelineStepTypes.BuildExecute, Category = "Build", Description = "Execute the build", Version = "1.0.0", Inputs = new() { new("projectPath", true), new("buildSystem", true), new("configuration", false) }, Outputs = new() { "success", "artifacts" } },

            // Memory steps
            new() { TypeId = PipelineStepTypes.MemoryLoad, Category = "Memory", Description = "Load project memory", Version = "1.0.0", Inputs = new() { new("projectId", false) }, Outputs = new() { "memory" } },
            new() { TypeId = PipelineStepTypes.MemorySave, Category = "Memory", Description = "Save project memory", Version = "1.0.0", Inputs = new() { new("projectId", false), new("memory", true) }, Outputs = new() { "success" } }
        };

        return Ok(steps);
    }

    private static PipelineDto MapToDto(PipelineDefinition pipeline)
    {
        return new PipelineDto
        {
            Id = pipeline.Id,
            Name = pipeline.Name,
            Description = pipeline.Description,
            Version = pipeline.Version,
            Status = pipeline.Status.ToString(),
            StepCount = pipeline.Steps.Count,
            CreatedAt = pipeline.CreatedAt,
            UpdatedAt = pipeline.UpdatedAt,
            Steps = pipeline.Steps.Select(s => new StepDto
            {
                Id = s.Id,
                Type = s.Type,
                Description = s.Description,
                DependsOn = s.DependsOn,
                Config = s.Config,
                Order = s.Order
            }).ToList(),
            Config = new PipelineConfigDto
            {
                TriggerKeywords = pipeline.Config.TriggerKeywords,
                IncludedPrompts = pipeline.Config.IncludedPrompts,
                EnabledTools = pipeline.Config.EnabledTools,
                InjectConversation = pipeline.Config.InjectConversation,
                MaxContextTokens = pipeline.Config.MaxContextTokens,
                MaxToolIterations = pipeline.Config.MaxToolIterations,
                Temperature = pipeline.Config.Temperature,
                MaxOutputTokens = pipeline.Config.MaxOutputTokens,
                EnablePlanning = pipeline.Config.EnablePlanning,
                EnableValidation = pipeline.Config.EnableValidation,
                PromptOverrides = pipeline.Config.PromptOverrides != null ? new PromptOverridesDto
                {
                    SystemPrompt = pipeline.Config.PromptOverrides.SystemPrompt,
                    DeveloperPrompt = pipeline.Config.PromptOverrides.DeveloperPrompt,
                    ToolsPrompt = pipeline.Config.PromptOverrides.ToolsPrompt,
                    PlanningPrompt = pipeline.Config.PromptOverrides.PlanningPrompt,
                    ValidationPrompt = pipeline.Config.PromptOverrides.ValidationPrompt
                } : null
            }
        };
    }

    private static PipelineDefinition MapFromDto(PipelineDto dto)
    {
        return new PipelineDefinition
        {
            Id = dto.Id ?? Guid.NewGuid().ToString(),
            Name = dto.Name,
            Description = dto.Description,
            Version = dto.Version,
            Status = Enum.TryParse<PipelineStatus>(dto.Status, out var status) ? status : PipelineStatus.Draft,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Steps = dto.Steps?.Select(s => new PipelineStep
            {
                Id = s.Id,
                Type = s.Type,
                Description = s.Description,
                DependsOn = s.DependsOn,
                Config = s.Config,
                Order = s.Order
            }).ToList() ?? new List<PipelineStep>(),
            Config = dto.Config != null ? new PipelineConfig
            {
                TriggerKeywords = dto.Config.TriggerKeywords ?? new(),
                IncludedPrompts = dto.Config.IncludedPrompts ?? new(),
                EnabledTools = dto.Config.EnabledTools ?? new(),
                InjectConversation = dto.Config.InjectConversation,
                MaxContextTokens = dto.Config.MaxContextTokens,
                MaxToolIterations = dto.Config.MaxToolIterations,
                Temperature = dto.Config.Temperature,
                MaxOutputTokens = dto.Config.MaxOutputTokens,
                EnablePlanning = dto.Config.EnablePlanning,
                EnableValidation = dto.Config.EnableValidation,
                PromptOverrides = dto.Config.PromptOverrides != null ? new PromptOverrides
                {
                    SystemPrompt = dto.Config.PromptOverrides.SystemPrompt,
                    DeveloperPrompt = dto.Config.PromptOverrides.DeveloperPrompt,
                    ToolsPrompt = dto.Config.PromptOverrides.ToolsPrompt,
                    PlanningPrompt = dto.Config.PromptOverrides.PlanningPrompt,
                    ValidationPrompt = dto.Config.PromptOverrides.ValidationPrompt
                } : null
            } : new PipelineConfig()
        };
    }
}

// DTOs
public class PipelinesResponse
{
    public List<PipelineDto> Pipelines { get; set; } = new();
    public string ActivePipelineId { get; set; } = string.Empty;
    public string PrimaryPipelineId { get; set; } = string.Empty;
}

public class PipelineDto
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Status { get; set; } = "Draft";
    public int StepCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<StepDto>? Steps { get; set; }
    public PipelineConfigDto? Config { get; set; }
}

public class StepDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? DependsOn { get; set; }
    public Dictionary<string, object>? Config { get; set; }
    public int Order { get; set; }
}

public class PipelineConfigDto
{
    public List<string>? TriggerKeywords { get; set; }
    public List<string>? IncludedPrompts { get; set; }
    public List<string>? EnabledTools { get; set; }
    public bool InjectConversation { get; set; } = true;
    public int MaxContextTokens { get; set; } = 4096;
    public int MaxToolIterations { get; set; } = 10;
    public float Temperature { get; set; } = 0.7f;
    public int MaxOutputTokens { get; set; } = 2048;
    public bool EnablePlanning { get; set; } = true;
    public bool EnableValidation { get; set; } = true;
    public PromptOverridesDto? PromptOverrides { get; set; }
}

public class PromptOverridesDto
{
    public string? SystemPrompt { get; set; }
    public string? DeveloperPrompt { get; set; }
    public string? ToolsPrompt { get; set; }
    public string? PlanningPrompt { get; set; }
    public string? ValidationPrompt { get; set; }
}

public class StepRegistryItemDto
{
    public string TypeId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<StepInputDto> Inputs { get; set; } = new();
    public List<string> Outputs { get; set; } = new();
}

public class StepInputDto
{
    public string Name { get; set; }
    public bool Required { get; set; }

    public StepInputDto() { Name = string.Empty; }
    public StepInputDto(string name, bool required) { Name = name; Required = required; }
}
