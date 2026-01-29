using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Pipeline.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LittleHelperAI.Controllers.Admin;

/// <summary>
/// Admin API for V2 Pipeline management.
/// </summary>
[ApiController]
[Route("api/admin/pipelines/v2")]
[Authorize(Roles = "Admin")]
public class PipelineAdminV2Controller : ControllerBase
{
    private readonly IPipelineStore _pipelineStore;
    private readonly IPipelineExecutionStore _executionStore;
    private readonly IStepRegistry _stepRegistry;
    private readonly IPipelineEngine _pipelineEngine;
    private readonly ILogger<PipelineAdminV2Controller> _logger;

    public PipelineAdminV2Controller(
        IPipelineStore pipelineStore,
        IPipelineExecutionStore executionStore,
        IStepRegistry stepRegistry,
        IPipelineEngine pipelineEngine,
        ILogger<PipelineAdminV2Controller> logger)
    {
        _pipelineStore = pipelineStore;
        _executionStore = executionStore;
        _stepRegistry = stepRegistry;
        _pipelineEngine = pipelineEngine;
        _logger = logger;
    }

    // ========================================================================
    // PIPELINE CRUD
    // ========================================================================

    /// <summary>
    /// List all pipelines.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PipelineListItem>>> GetAll(CancellationToken cancellationToken)
    {
        var pipelines = await _pipelineStore.GetAllAsync(cancellationToken);

        return Ok(pipelines.Select(p => new PipelineListItem
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Version = p.Version,
            Status = p.Status.ToString(),
            IsPrimary = p.IsPrimary,
            StepCount = p.Steps.Count,
            Tags = p.Tags,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }));
    }

    /// <summary>
    /// Get a pipeline by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PipelineDefinitionV2>> GetById(string id, CancellationToken cancellationToken)
    {
        var pipeline = await _pipelineStore.GetByIdAsync(id, cancellationToken);
        if (pipeline == null)
        {
            return NotFound();
        }

        return Ok(pipeline);
    }

    /// <summary>
    /// Create a new pipeline.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PipelineDefinitionV2>> Create(
        [FromBody] PipelineDefinitionV2 pipeline,
        CancellationToken cancellationToken)
    {
        // Validate
        var validation = _pipelineEngine.ValidatePipeline(pipeline);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        var userId = GetUserId();
        var created = await _pipelineStore.CreateAsync(pipeline, userId, cancellationToken);

        _logger.LogInformation("User {UserId} created pipeline {PipelineId}", userId, created.Id);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Update a pipeline.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<PipelineDefinitionV2>> Update(
        string id,
        [FromBody] PipelineDefinitionV2 pipeline,
        CancellationToken cancellationToken)
    {
        pipeline.Id = id;

        // Validate
        var validation = _pipelineEngine.ValidatePipeline(pipeline);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        try
        {
            var updated = await _pipelineStore.UpdateAsync(pipeline, cancellationToken);
            _logger.LogInformation("User {UserId} updated pipeline {PipelineId}", GetUserId(), id);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a pipeline.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var deleted = await _pipelineStore.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound();
        }

        _logger.LogInformation("User {UserId} deleted pipeline {PipelineId}", GetUserId(), id);
        return NoContent();
    }

    // ========================================================================
    // PIPELINE STATUS
    // ========================================================================

    /// <summary>
    /// Activate a pipeline.
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<ActionResult> Activate(string id, CancellationToken cancellationToken)
    {
        var result = await _pipelineStore.ActivateAsync(id, cancellationToken);
        if (!result)
        {
            return NotFound();
        }

        _logger.LogInformation("User {UserId} activated pipeline {PipelineId}", GetUserId(), id);
        return Ok(new { message = "Pipeline activated" });
    }

    /// <summary>
    /// Deactivate a pipeline.
    /// </summary>
    [HttpPost("{id}/deactivate")]
    public async Task<ActionResult> Deactivate(string id, CancellationToken cancellationToken)
    {
        var result = await _pipelineStore.DeactivateAsync(id, cancellationToken);
        if (!result)
        {
            return NotFound();
        }

        _logger.LogInformation("User {UserId} deactivated pipeline {PipelineId}", GetUserId(), id);
        return Ok(new { message = "Pipeline deactivated" });
    }

    /// <summary>
    /// Set a pipeline as primary.
    /// </summary>
    [HttpPost("primary/{id}")]
    public async Task<ActionResult> SetPrimary(string id, CancellationToken cancellationToken)
    {
        var result = await _pipelineStore.SetPrimaryAsync(id, cancellationToken);
        if (!result)
        {
            return NotFound();
        }

        _logger.LogInformation("User {UserId} set primary pipeline to {PipelineId}", GetUserId(), id);
        return Ok(new { message = "Primary pipeline updated" });
    }

    // ========================================================================
    // VERSIONING
    // ========================================================================

    /// <summary>
    /// Get version history for a pipeline.
    /// </summary>
    [HttpGet("{id}/versions")]
    public async Task<ActionResult<IReadOnlyList<PipelineVersionInfo>>> GetVersions(
        string id,
        CancellationToken cancellationToken)
    {
        var versions = await _pipelineStore.GetVersionsAsync(id, cancellationToken);
        return Ok(versions);
    }

    /// <summary>
    /// Create a new version of a pipeline.
    /// </summary>
    [HttpPost("{id}/versions")]
    public async Task<ActionResult<PipelineVersionInfo>> CreateVersion(
        string id,
        [FromBody] CreateVersionRequest request,
        CancellationToken cancellationToken)
    {
        var version = await _pipelineStore.CreateVersionAsync(id, request.CommitMessage, GetUserId(), cancellationToken);
        _logger.LogInformation("User {UserId} created version {Version} for pipeline {PipelineId}", GetUserId(), version.Version, id);
        return Ok(version);
    }

    /// <summary>
    /// Rollback to a previous version.
    /// </summary>
    [HttpPost("{id}/rollback/{version}")]
    public async Task<ActionResult<PipelineDefinitionV2>> Rollback(
        string id,
        string version,
        CancellationToken cancellationToken)
    {
        var pipeline = await _pipelineStore.RollbackToVersionAsync(id, version, cancellationToken);
        if (pipeline == null)
        {
            return NotFound(new { error = "Version not found" });
        }

        _logger.LogInformation("User {UserId} rolled back pipeline {PipelineId} to version {Version}", GetUserId(), id, version);
        return Ok(pipeline);
    }

    // ========================================================================
    // TESTING
    // ========================================================================

    /// <summary>
    /// Test a pipeline with sample input.
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestResult>> TestPipeline(
        string id,
        [FromBody] TestRequest request,
        CancellationToken cancellationToken)
    {
        var pipeline = await _pipelineStore.GetByIdAsync(id, cancellationToken);
        if (pipeline == null)
        {
            return NotFound();
        }

        var input = new PipelineInput
        {
            Message = request.Message,
            ProjectPath = request.ProjectPath
        };

        var result = await _pipelineEngine.ExecuteAsync(pipeline, input, cancellationToken);

        return Ok(new TestResult
        {
            Success = result.Success,
            Response = result.ResponseText,
            ErrorMessage = result.ErrorMessage,
            DurationMs = result.DurationMs,
            StepCount = result.Context?.TotalStepCount ?? 0,
            CompletedSteps = result.Context?.CompletedStepCount ?? 0,
            Events = result.Events.Select(e => new TestEventSummary
            {
                Type = e.Type.ToString(),
                StepId = e.StepId,
                Content = e.Content?.Length > 200 ? e.Content.Substring(0, 200) + "..." : e.Content
            }).ToList()
        });
    }

    // ========================================================================
    // EXECUTIONS
    // ========================================================================

    /// <summary>
    /// List executions.
    /// </summary>
    [HttpGet("executions")]
    public async Task<ActionResult<PagedResult<ExecutionSummary>>> GetExecutions(
        [FromQuery] string? pipelineId,
        [FromQuery] int? userId,
        [FromQuery] string? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new ExecutionFilter
        {
            PipelineId = pipelineId,
            UserId = userId,
            Status = status,
            StartDate = startDate,
            EndDate = endDate
        };

        var result = await _executionStore.GetExecutionsAsync(filter, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get execution details.
    /// </summary>
    [HttpGet("executions/{id}")]
    public async Task<ActionResult<ExecutionDetails>> GetExecution(string id, CancellationToken cancellationToken)
    {
        var details = await _executionStore.GetExecutionDetailsAsync(id, cancellationToken);
        if (details == null)
        {
            return NotFound();
        }

        return Ok(details);
    }

    // ========================================================================
    // STEP CATALOG
    // ========================================================================

    /// <summary>
    /// Get available step types.
    /// </summary>
    [HttpGet("steps")]
    public ActionResult<StepCatalog> GetStepCatalog()
    {
        return Ok(_stepRegistry.GetCatalog());
    }

    // ========================================================================
    // METRICS
    // ========================================================================

    /// <summary>
    /// Get aggregated metrics.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<ActionResult<PipelineMetrics>> GetMetrics(
        [FromQuery] string? pipelineId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var metrics = await _executionStore.GetMetricsAsync(pipelineId, startDate, endDate, cancellationToken);
        return Ok(metrics);
    }

    /// <summary>
    /// Get real-time metrics (last 24 hours).
    /// </summary>
    [HttpGet("metrics/realtime")]
    public async Task<ActionResult<PipelineMetrics>> GetRealtimeMetrics(
        [FromQuery] string? pipelineId,
        CancellationToken cancellationToken)
    {
        var metrics = await _executionStore.GetMetricsAsync(
            pipelineId,
            DateTime.UtcNow.AddHours(-24),
            DateTime.UtcNow,
            cancellationToken);
        return Ok(metrics);
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    private int? GetUserId()
    {
        var userIdClaim = User.FindFirst("UserId")?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}

// ============================================================================
// DTOs
// ============================================================================

public sealed class PipelineListItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Version { get; init; }
    public required string Status { get; init; }
    public bool IsPrimary { get; init; }
    public int StepCount { get; init; }
    public List<string> Tags { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class CreateVersionRequest
{
    public string? CommitMessage { get; init; }
}

public sealed class TestRequest
{
    public required string Message { get; init; }
    public string? ProjectPath { get; init; }
}

public sealed class TestResult
{
    public bool Success { get; init; }
    public string? Response { get; init; }
    public string? ErrorMessage { get; init; }
    public double DurationMs { get; init; }
    public int StepCount { get; init; }
    public int CompletedSteps { get; init; }
    public List<TestEventSummary> Events { get; init; } = new();
}

public sealed class TestEventSummary
{
    public required string Type { get; init; }
    public string? StepId { get; init; }
    public string? Content { get; init; }
}
