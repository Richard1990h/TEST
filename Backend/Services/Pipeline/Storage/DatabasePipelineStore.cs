using System.Text.Json;
using System.Text.RegularExpressions;
using LittleHelperAI.Data;
using LittleHelperAI.Data.Entities;
using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Pipeline.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.Backend.Services.Pipeline.Storage;

/// <summary>
/// Database-backed implementation of pipeline storage using EF Core.
/// </summary>
public sealed class DatabasePipelineStore : IPipelineStore
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<DatabasePipelineStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DatabasePipelineStore(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<DatabasePipelineStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<IReadOnlyList<PipelineDefinitionV2>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.Set<PipelineV2Entity>()
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDefinition).ToList();
    }

    public async Task<PipelineDefinitionV2?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Set<PipelineV2Entity>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return entity != null ? ToDefinition(entity) : null;
    }

    public async Task<PipelineDefinitionV2?> GetPrimaryAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Set<PipelineV2Entity>()
            .Where(p => p.IsPrimary && p.Status == "Active")
            .FirstOrDefaultAsync(cancellationToken);

        if (entity == null)
        {
            // Fall back to first active pipeline
            entity = await context.Set<PipelineV2Entity>()
                .Where(p => p.Status == "Active")
                .OrderBy(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return entity != null ? ToDefinition(entity) : null;
    }

    public async Task<PipelineDefinitionV2?> GetForMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var activePipelines = await context.Set<PipelineV2Entity>()
            .Where(p => p.Status == "Active")
            .ToListAsync(cancellationToken);

        var lowerMessage = message.ToLowerInvariant();

        foreach (var entity in activePipelines.OrderByDescending(p => p.IsPrimary ? 0 : 1))
        {
            var definition = ToDefinition(entity);

            // Check keywords
            foreach (var keyword in definition.Config.TriggerKeywords)
            {
                if (lowerMessage.Contains(keyword.ToLowerInvariant()))
                {
                    _logger.LogDebug("Message matched keyword '{Keyword}' for pipeline {PipelineId}", keyword, definition.Id);
                    return definition;
                }
            }

            // Check patterns
            foreach (var pattern in definition.Config.TriggerPatterns)
            {
                try
                {
                    if (Regex.IsMatch(message, pattern, RegexOptions.IgnoreCase))
                    {
                        _logger.LogDebug("Message matched pattern '{Pattern}' for pipeline {PipelineId}", pattern, definition.Id);
                        return definition;
                    }
                }
                catch (RegexParseException)
                {
                    _logger.LogWarning("Invalid regex pattern in pipeline {PipelineId}: {Pattern}", definition.Id, pattern);
                }
            }
        }

        // Return primary if no match
        return await GetPrimaryAsync(cancellationToken);
    }

    public async Task<PipelineDefinitionV2> CreateAsync(PipelineDefinitionV2 pipeline, int? createdBy = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = ToEntity(pipeline);
        entity.CreatedBy = createdBy;

        context.Set<PipelineV2Entity>().Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created pipeline {PipelineId}: {Name}", pipeline.Id, pipeline.Name);
        return ToDefinition(entity);
    }

    public async Task<PipelineDefinitionV2> UpdateAsync(PipelineDefinitionV2 pipeline, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Set<PipelineV2Entity>()
            .FirstOrDefaultAsync(p => p.Id == pipeline.Id, cancellationToken);

        if (entity == null)
        {
            throw new InvalidOperationException($"Pipeline {pipeline.Id} not found");
        }

        // Update fields
        entity.Name = pipeline.Name;
        entity.Description = pipeline.Description;
        entity.Version = pipeline.Version;
        entity.Status = pipeline.Status.ToString();
        entity.IsPrimary = pipeline.IsPrimary;
        entity.ConfigJson = JsonSerializer.Serialize(pipeline, _jsonOptions);
        entity.Tags = pipeline.Tags.Count > 0 ? string.Join(",", pipeline.Tags) : null;
        entity.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated pipeline {PipelineId}: {Name}", pipeline.Id, pipeline.Name);
        return ToDefinition(entity);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Set<PipelineV2Entity>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (entity == null)
        {
            return false;
        }

        context.Set<PipelineV2Entity>().Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted pipeline {PipelineId}", id);
        return true;
    }

    public async Task<bool> SetPrimaryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Clear all primary flags
        await context.Set<PipelineV2Entity>()
            .Where(p => p.IsPrimary)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPrimary, false), cancellationToken);

        // Set new primary
        var updated = await context.Set<PipelineV2Entity>()
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPrimary, true), cancellationToken);

        if (updated > 0)
        {
            _logger.LogInformation("Set primary pipeline to {PipelineId}", id);
        }

        return updated > 0;
    }

    public async Task<bool> ActivateAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var updated = await context.Set<PipelineV2Entity>()
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "Active")
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), cancellationToken);

        if (updated > 0)
        {
            _logger.LogInformation("Activated pipeline {PipelineId}", id);
        }

        return updated > 0;
    }

    public async Task<bool> DeactivateAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var updated = await context.Set<PipelineV2Entity>()
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "Inactive")
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), cancellationToken);

        if (updated > 0)
        {
            _logger.LogInformation("Deactivated pipeline {PipelineId}", id);
        }

        return updated > 0;
    }

    public async Task<IReadOnlyList<PipelineVersionInfo>> GetVersionsAsync(string pipelineId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var versions = await context.Set<PipelineVersionEntity>()
            .Where(v => v.PipelineId == pipelineId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(cancellationToken);

        return versions.Select(v => new PipelineVersionInfo
        {
            Id = v.Id,
            PipelineId = v.PipelineId,
            Version = v.Version,
            CommitMessage = v.CommitMessage,
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy
        }).ToList();
    }

    public async Task<PipelineVersionInfo> CreateVersionAsync(string pipelineId, string? commitMessage = null, int? createdBy = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var pipeline = await context.Set<PipelineV2Entity>()
            .FirstOrDefaultAsync(p => p.Id == pipelineId, cancellationToken);

        if (pipeline == null)
        {
            throw new InvalidOperationException($"Pipeline {pipelineId} not found");
        }

        var version = new PipelineVersionEntity
        {
            PipelineId = pipelineId,
            Version = pipeline.Version,
            ConfigJson = pipeline.ConfigJson,
            CommitMessage = commitMessage,
            CreatedBy = createdBy
        };

        context.Set<PipelineVersionEntity>().Add(version);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created version {Version} for pipeline {PipelineId}", version.Version, pipelineId);

        return new PipelineVersionInfo
        {
            Id = version.Id,
            PipelineId = version.PipelineId,
            Version = version.Version,
            CommitMessage = version.CommitMessage,
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedBy
        };
    }

    public async Task<PipelineDefinitionV2?> RollbackToVersionAsync(string pipelineId, string version, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var versionEntity = await context.Set<PipelineVersionEntity>()
            .Where(v => v.PipelineId == pipelineId && v.Version == version)
            .FirstOrDefaultAsync(cancellationToken);

        if (versionEntity == null)
        {
            _logger.LogWarning("Version {Version} not found for pipeline {PipelineId}", version, pipelineId);
            return null;
        }

        var pipeline = await context.Set<PipelineV2Entity>()
            .FirstOrDefaultAsync(p => p.Id == pipelineId, cancellationToken);

        if (pipeline == null)
        {
            return null;
        }

        // Restore configuration from version
        pipeline.ConfigJson = versionEntity.ConfigJson;
        pipeline.Version = versionEntity.Version;
        pipeline.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Rolled back pipeline {PipelineId} to version {Version}", pipelineId, version);
        return ToDefinition(pipeline);
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var hasAny = await context.Set<PipelineV2Entity>().AnyAsync(cancellationToken);
        if (hasAny)
        {
            _logger.LogDebug("Pipelines already exist, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding default pipelines...");

        foreach (var pipeline in DefaultPipelines.GetAll())
        {
            var entity = ToEntity(pipeline);
            context.Set<PipelineV2Entity>().Add(entity);
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} default pipelines", DefaultPipelines.GetAll().Count);
    }

    private PipelineDefinitionV2 ToDefinition(PipelineV2Entity entity)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<PipelineDefinitionV2>(entity.ConfigJson, _jsonOptions);
            if (definition != null)
            {
                // Ensure ID matches
                definition.Id = entity.Id;
                definition.Name = entity.Name;
                definition.IsPrimary = entity.IsPrimary;
                definition.CreatedAt = entity.CreatedAt;
                definition.UpdatedAt = entity.UpdatedAt;

                if (Enum.TryParse<PipelineStatusV2>(entity.Status, out var status))
                {
                    definition.Status = status;
                }

                return definition;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize pipeline {PipelineId}, creating minimal definition", entity.Id);
        }

        // Return minimal definition if parsing fails
        return new PipelineDefinitionV2
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description ?? "",
            Version = entity.Version,
            Status = Enum.TryParse<PipelineStatusV2>(entity.Status, out var s) ? s : PipelineStatusV2.Draft,
            IsPrimary = entity.IsPrimary,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private PipelineV2Entity ToEntity(PipelineDefinitionV2 definition)
    {
        return new PipelineV2Entity
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Version = definition.Version,
            Status = definition.Status.ToString(),
            IsPrimary = definition.IsPrimary,
            ConfigJson = JsonSerializer.Serialize(definition, _jsonOptions),
            Tags = definition.Tags.Count > 0 ? string.Join(",", definition.Tags) : null,
            CreatedAt = definition.CreatedAt,
            UpdatedAt = definition.UpdatedAt
        };
    }
}
