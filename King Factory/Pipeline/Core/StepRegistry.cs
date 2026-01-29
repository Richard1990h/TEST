using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// Registry of all available pipeline step types.
/// Steps are registered at startup and looked up by TypeId during execution.
/// </summary>
public interface IStepRegistry
{
    /// <summary>
    /// Register a step type.
    /// </summary>
    void Register(IPipelineStep step);

    /// <summary>
    /// Register multiple step types.
    /// </summary>
    void RegisterAll(IEnumerable<IPipelineStep> steps);

    /// <summary>
    /// Get a step by its type ID.
    /// </summary>
    IPipelineStep? GetStep(string typeId);

    /// <summary>
    /// Get a step by its type ID, throwing if not found.
    /// </summary>
    IPipelineStep GetStepRequired(string typeId);

    /// <summary>
    /// Check if a step type is registered.
    /// </summary>
    bool HasStep(string typeId);

    /// <summary>
    /// Get all registered steps.
    /// </summary>
    IReadOnlyList<IPipelineStep> GetAllSteps();

    /// <summary>
    /// Get all steps in a category.
    /// </summary>
    IReadOnlyList<IPipelineStep> GetStepsByCategory(string category);

    /// <summary>
    /// Get all available categories.
    /// </summary>
    IReadOnlyList<string> GetCategories();

    /// <summary>
    /// Get step catalog for admin UI.
    /// </summary>
    StepCatalog GetCatalog();
}

/// <summary>
/// Default implementation of the step registry.
/// </summary>
public sealed class StepRegistry : IStepRegistry
{
    private readonly ILogger<StepRegistry> _logger;
    private readonly ConcurrentDictionary<string, IPipelineStep> _steps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _catalogLock = new();
    private StepCatalog? _cachedCatalog;

    public StepRegistry(ILogger<StepRegistry> logger)
    {
        _logger = logger;
    }

    public void Register(IPipelineStep step)
    {
        if (string.IsNullOrWhiteSpace(step.TypeId))
        {
            throw new ArgumentException("Step TypeId cannot be null or empty", nameof(step));
        }

        if (_steps.TryAdd(step.TypeId, step))
        {
            _logger.LogDebug("Registered step type: {TypeId} ({Category})", step.TypeId, step.Category);
            InvalidateCatalog();
        }
        else
        {
            _logger.LogWarning("Step type already registered: {TypeId}", step.TypeId);
        }
    }

    public void RegisterAll(IEnumerable<IPipelineStep> steps)
    {
        foreach (var step in steps)
        {
            Register(step);
        }
    }

    public IPipelineStep? GetStep(string typeId)
    {
        _steps.TryGetValue(typeId, out var step);
        return step;
    }

    public IPipelineStep GetStepRequired(string typeId)
    {
        if (!_steps.TryGetValue(typeId, out var step))
        {
            throw new StepConfigurationException($"Unknown step type: {typeId}");
        }
        return step;
    }

    public bool HasStep(string typeId)
    {
        return _steps.ContainsKey(typeId);
    }

    public IReadOnlyList<IPipelineStep> GetAllSteps()
    {
        return _steps.Values.ToList();
    }

    public IReadOnlyList<IPipelineStep> GetStepsByCategory(string category)
    {
        return _steps.Values
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<string> GetCategories()
    {
        return _steps.Values
            .Select(s => s.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();
    }

    public StepCatalog GetCatalog()
    {
        lock (_catalogLock)
        {
            if (_cachedCatalog != null)
                return _cachedCatalog;

            var categories = new List<StepCategoryInfo>();

            foreach (var category in GetCategories())
            {
                var steps = GetStepsByCategory(category)
                    .Select(s => new StepTypeInfo
                    {
                        TypeId = s.TypeId,
                        DisplayName = s.DisplayName,
                        Description = s.Description,
                        Category = s.Category,
                        SupportsStreaming = s.SupportsStreaming,
                        IsAsyncOnly = s.IsAsyncOnly,
                        ParameterSchema = s.ParameterSchema
                    })
                    .OrderBy(s => s.DisplayName)
                    .ToList();

                categories.Add(new StepCategoryInfo
                {
                    Name = category,
                    Steps = steps
                });
            }

            _cachedCatalog = new StepCatalog
            {
                Categories = categories.OrderBy(c => GetCategoryOrder(c.Name)).ToList(),
                TotalStepCount = _steps.Count
            };

            return _cachedCatalog;
        }
    }

    private void InvalidateCatalog()
    {
        lock (_catalogLock)
        {
            _cachedCatalog = null;
        }
    }

    private static int GetCategoryOrder(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "inject" => 0,
            "transform" => 1,
            "llm" => 2,
            "tool" => 3,
            "control" => 4,
            "validate" => 5,
            _ => 99
        };
    }
}

/// <summary>
/// Catalog of all available step types for admin UI.
/// </summary>
public sealed class StepCatalog
{
    public IReadOnlyList<StepCategoryInfo> Categories { get; init; } = Array.Empty<StepCategoryInfo>();
    public int TotalStepCount { get; init; }
}

/// <summary>
/// Information about a step category.
/// </summary>
public sealed class StepCategoryInfo
{
    public required string Name { get; init; }
    public IReadOnlyList<StepTypeInfo> Steps { get; init; } = Array.Empty<StepTypeInfo>();
}

/// <summary>
/// Information about a step type.
/// </summary>
public sealed class StepTypeInfo
{
    public required string TypeId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool IsAsyncOnly { get; init; }
    public StepParameterSchema ParameterSchema { get; init; } = StepParameterSchema.Empty;
}
