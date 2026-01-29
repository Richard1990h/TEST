using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Tools;

/// <summary>
/// Registry for all available tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Get a tool by name.
    /// </summary>
    ITool? GetTool(string name);

    /// <summary>
    /// Get all registered tools.
    /// </summary>
    IReadOnlyList<ITool> GetAllTools();

    /// <summary>
    /// Check if a tool exists.
    /// </summary>
    bool HasTool(string name);

    /// <summary>
    /// Get tool manifest for LLM prompt injection.
    /// </summary>
    string GetToolManifest();
}

/// <summary>
/// Implementation of tool registry using DI.
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(IEnumerable<ITool> tools, ILogger<ToolRegistry> logger)
    {
        _logger = logger;
        _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            if (_tools.ContainsKey(tool.Name))
            {
                _logger.LogWarning("Duplicate tool name: {ToolName}", tool.Name);
                continue;
            }

            _tools[tool.Name] = tool;
            _logger.LogDebug("Registered tool: {ToolName}", tool.Name);
        }

        _logger.LogInformation("Tool registry initialized with {Count} tools", _tools.Count);
    }

    public ITool? GetTool(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    public IReadOnlyList<ITool> GetAllTools()
    {
        return _tools.Values.ToList();
    }

    public bool HasTool(string name)
    {
        return _tools.ContainsKey(name);
    }

    public string GetToolManifest()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Available Tools\n");

        foreach (var tool in _tools.Values.OrderBy(t => t.Name))
        {
            sb.AppendLine($"### {tool.Name}");
            sb.AppendLine(tool.Description);
            sb.AppendLine();

            if (tool.Schema.Properties.Any())
            {
                sb.AppendLine("**Parameters:**");
                foreach (var prop in tool.Schema.Properties)
                {
                    var required = tool.Schema.Required.Contains(prop.Key) ? "(required)" : "(optional)";
                    sb.AppendLine($"- `{prop.Key}` ({prop.Value.Type}) {required}: {prop.Value.Description}");
                }
                sb.AppendLine();
            }

            if (tool.RequiresConfirmation)
            {
                sb.AppendLine("*Requires user confirmation*\n");
            }
        }

        return sb.ToString();
    }
}
