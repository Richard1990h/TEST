namespace LittleHelperAI.KingFactory.Pipeline.Core;

/// <summary>
/// Builds a directed acyclic graph (DAG) from step dependencies
/// and determines execution order.
/// </summary>
public interface IDependencyGraphBuilder
{
    /// <summary>
    /// Build a dependency graph from a pipeline definition.
    /// </summary>
    DependencyGraph Build(PipelineDefinitionV2 pipeline);

    /// <summary>
    /// Validate that the dependency graph is a valid DAG (no cycles).
    /// </summary>
    DependencyValidationResult Validate(DependencyGraph graph);
}

/// <summary>
/// Default dependency graph builder implementation.
/// </summary>
public sealed class DependencyGraphBuilder : IDependencyGraphBuilder
{
    public DependencyGraph Build(PipelineDefinitionV2 pipeline)
    {
        var nodes = new Dictionary<string, DependencyNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<DependencyEdge>();

        // Create nodes for each step
        foreach (var step in pipeline.Steps)
        {
            nodes[step.Id] = new DependencyNode
            {
                StepId = step.Id,
                StepType = step.Type,
                Order = step.Order,
                Dependencies = step.DependsOn?.ToList() ?? new List<string>(),
                Dependents = new List<string>()
            };
        }

        // Build edges and dependent lists
        foreach (var step in pipeline.Steps)
        {
            if (step.DependsOn == null) continue;

            foreach (var depId in step.DependsOn)
            {
                if (nodes.TryGetValue(depId, out var depNode))
                {
                    depNode.Dependents.Add(step.Id);
                    edges.Add(new DependencyEdge
                    {
                        FromStepId = depId,
                        ToStepId = step.Id
                    });
                }
            }
        }

        // Calculate execution levels using topological sort
        var levels = CalculateLevels(nodes.Values.ToList());

        foreach (var (stepId, level) in levels)
        {
            if (nodes.TryGetValue(stepId, out var node))
            {
                node.Level = level;
            }
        }

        return new DependencyGraph
        {
            Nodes = nodes.Values.ToList(),
            Edges = edges,
            ExecutionOrder = GetExecutionOrder(nodes.Values.ToList(), levels)
        };
    }

    public DependencyValidationResult Validate(DependencyGraph graph)
    {
        var errors = new List<string>();

        // Check for missing dependencies
        var allStepIds = graph.Nodes.Select(n => n.StepId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            foreach (var dep in node.Dependencies)
            {
                if (!allStepIds.Contains(dep))
                {
                    errors.Add($"Step '{node.StepId}' depends on unknown step '{dep}'");
                }
            }
        }

        // Check for cycles using DFS
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node.StepId))
            {
                var cyclePath = DetectCycle(node.StepId, graph, visited, inStack, new List<string>());
                if (cyclePath != null)
                {
                    errors.Add($"Cycle detected: {string.Join(" -> ", cyclePath)}");
                }
            }
        }

        return new DependencyValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private Dictionary<string, int> CalculateLevels(IReadOnlyList<DependencyNode> nodes)
    {
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Initialize
        foreach (var node in nodes)
        {
            inDegree[node.StepId] = node.Dependencies.Count;
            adjacency[node.StepId] = new List<string>();
        }

        // Build adjacency list (from dependency to dependent)
        foreach (var node in nodes)
        {
            foreach (var dep in node.Dependencies)
            {
                if (adjacency.ContainsKey(dep))
                {
                    adjacency[dep].Add(node.StepId);
                }
            }
        }

        // BFS to assign levels
        var queue = new Queue<string>();
        foreach (var node in nodes)
        {
            if (inDegree[node.StepId] == 0)
            {
                queue.Enqueue(node.StepId);
                levels[node.StepId] = 0;
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentLevel = levels[current];

            foreach (var dependent in adjacency[current])
            {
                inDegree[dependent]--;

                // Update level to be max of all dependencies + 1
                var newLevel = currentLevel + 1;
                if (!levels.ContainsKey(dependent) || levels[dependent] < newLevel)
                {
                    levels[dependent] = newLevel;
                }

                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        // Handle nodes not in the graph (orphans) - assign level based on Order
        foreach (var node in nodes)
        {
            if (!levels.ContainsKey(node.StepId))
            {
                levels[node.StepId] = node.Order;
            }
        }

        return levels;
    }

    private List<string> GetExecutionOrder(IReadOnlyList<DependencyNode> nodes, Dictionary<string, int> levels)
    {
        return nodes
            .OrderBy(n => levels.GetValueOrDefault(n.StepId, n.Order))
            .ThenBy(n => n.Order)
            .ThenBy(n => n.StepId)
            .Select(n => n.StepId)
            .ToList();
    }

    private List<string>? DetectCycle(
        string nodeId,
        DependencyGraph graph,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path)
    {
        visited.Add(nodeId);
        inStack.Add(nodeId);
        path.Add(nodeId);

        var node = graph.Nodes.FirstOrDefault(n => n.StepId == nodeId);
        if (node != null)
        {
            foreach (var dependent in node.Dependents)
            {
                if (!visited.Contains(dependent))
                {
                    var cyclePath = DetectCycle(dependent, graph, visited, inStack, new List<string>(path));
                    if (cyclePath != null)
                        return cyclePath;
                }
                else if (inStack.Contains(dependent))
                {
                    path.Add(dependent);
                    return path;
                }
            }
        }

        inStack.Remove(nodeId);
        return null;
    }
}

/// <summary>
/// Represents a dependency graph of pipeline steps.
/// </summary>
public sealed class DependencyGraph
{
    /// <summary>
    /// All nodes in the graph.
    /// </summary>
    public IReadOnlyList<DependencyNode> Nodes { get; init; } = Array.Empty<DependencyNode>();

    /// <summary>
    /// All edges in the graph.
    /// </summary>
    public IReadOnlyList<DependencyEdge> Edges { get; init; } = Array.Empty<DependencyEdge>();

    /// <summary>
    /// Ordered list of step IDs for execution.
    /// </summary>
    public IReadOnlyList<string> ExecutionOrder { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Get steps that can be executed in parallel at a given level.
    /// </summary>
    public IReadOnlyList<string> GetStepsAtLevel(int level)
    {
        return Nodes
            .Where(n => n.Level == level)
            .Select(n => n.StepId)
            .ToList();
    }

    /// <summary>
    /// Get the maximum level (depth) of the graph.
    /// </summary>
    public int MaxLevel => Nodes.Count > 0 ? Nodes.Max(n => n.Level) : 0;

    /// <summary>
    /// Get steps that have no dependencies (roots).
    /// </summary>
    public IReadOnlyList<string> GetRootSteps()
    {
        return Nodes
            .Where(n => n.Dependencies.Count == 0)
            .Select(n => n.StepId)
            .ToList();
    }

    /// <summary>
    /// Check if a step's dependencies are all complete.
    /// </summary>
    public bool AreDependenciesComplete(string stepId, ISet<string> completedSteps)
    {
        var node = Nodes.FirstOrDefault(n => n.StepId == stepId);
        if (node == null)
            return true;

        return node.Dependencies.All(d => completedSteps.Contains(d));
    }
}

/// <summary>
/// A node in the dependency graph.
/// </summary>
public sealed class DependencyNode
{
    public required string StepId { get; init; }
    public required string StepType { get; init; }
    public int Order { get; init; }
    public int Level { get; set; }
    public List<string> Dependencies { get; init; } = new();
    public List<string> Dependents { get; init; } = new();
}

/// <summary>
/// An edge in the dependency graph.
/// </summary>
public sealed class DependencyEdge
{
    public required string FromStepId { get; init; }
    public required string ToStepId { get; init; }
}

/// <summary>
/// Result of validating a dependency graph.
/// </summary>
public sealed class DependencyValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
