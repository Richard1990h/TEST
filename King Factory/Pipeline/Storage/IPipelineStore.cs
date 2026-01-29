using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Storage;

/// <summary>
/// Storage interface for V2 pipelines.
/// </summary>
public interface IPipelineStore
{
    /// <summary>
    /// Get all pipelines.
    /// </summary>
    Task<IReadOnlyList<PipelineDefinitionV2>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a pipeline by ID.
    /// </summary>
    Task<PipelineDefinitionV2?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the primary (default) pipeline.
    /// </summary>
    Task<PipelineDefinitionV2?> GetPrimaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a pipeline that matches the given message (by triggers).
    /// </summary>
    Task<PipelineDefinitionV2?> GetForMessageAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new pipeline.
    /// </summary>
    Task<PipelineDefinitionV2> CreateAsync(PipelineDefinitionV2 pipeline, int? createdBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing pipeline.
    /// </summary>
    Task<PipelineDefinitionV2> UpdateAsync(PipelineDefinitionV2 pipeline, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a pipeline.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a pipeline as the primary pipeline.
    /// </summary>
    Task<bool> SetPrimaryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activate a pipeline.
    /// </summary>
    Task<bool> ActivateAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivate a pipeline.
    /// </summary>
    Task<bool> DeactivateAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get version history for a pipeline.
    /// </summary>
    Task<IReadOnlyList<PipelineVersionInfo>> GetVersionsAsync(string pipelineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new version of a pipeline.
    /// </summary>
    Task<PipelineVersionInfo> CreateVersionAsync(string pipelineId, string? commitMessage = null, int? createdBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback to a previous version.
    /// </summary>
    Task<PipelineDefinitionV2?> RollbackToVersionAsync(string pipelineId, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seed default pipelines if none exist.
    /// </summary>
    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a pipeline version.
/// </summary>
public sealed class PipelineVersionInfo
{
    public required string Id { get; init; }
    public required string PipelineId { get; init; }
    public required string Version { get; init; }
    public string? CommitMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
}
