using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace LittleHelperAI.KingFactory.Tools.Filesystem;

/// <summary>
/// Tool for listing directory contents.
/// </summary>
public class ListFilesTool : ITool
{
    private readonly ILogger<ListFilesTool> _logger;
    private readonly FilesystemConfig _config;

    public string Name => "list_files";
    public string Description => "List files and directories at the specified path.";
    public bool RequiresConfirmation => false;

    public ToolSchema Schema => new()
    {
        Properties = new Dictionary<string, ToolParameter>
        {
            ["path"] = new()
            {
                Type = "string",
                Description = "Path to the directory to list"
            },
            ["recursive"] = new()
            {
                Type = "boolean",
                Description = "Include subdirectories recursively",
                Default = false
            },
            ["pattern"] = new()
            {
                Type = "string",
                Description = "File pattern filter (e.g., '*.cs')",
                Default = "*"
            },
            ["maxDepth"] = new()
            {
                Type = "integer",
                Description = "Maximum recursion depth (default: 3)",
                Default = 3
            }
        },
        Required = new List<string> { "path" }
    };

    public ListFilesTool(ILogger<ListFilesTool> logger, FilesystemConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public ValidationResult ValidateArguments(Dictionary<string, object> arguments)
    {
        if (!arguments.TryGetValue("path", out var pathObj) || pathObj is not string path || string.IsNullOrWhiteSpace(path))
        {
            return ValidationResult.Invalid("'path' is required");
        }

        var fullPath = GetSecurePath(path);
        if (fullPath == null)
        {
            return ValidationResult.Invalid("Path is outside allowed directory");
        }

        return ValidationResult.Valid();
    }

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var path = arguments["path"].ToString()!;
        var fullPath = GetSecurePath(path);

        if (fullPath == null)
        {
            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = "Path is outside allowed directory"
            });
        }

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = $"Directory not found: {path}"
            });
        }

        try
        {
            var recursive = false;
            if (arguments.TryGetValue("recursive", out var recObj))
            {
                recursive = recObj is bool b ? b : bool.Parse(recObj.ToString() ?? "false");
            }

            var pattern = "*";
            if (arguments.TryGetValue("pattern", out var patObj) && patObj is string pat)
            {
                pattern = pat;
            }

            var maxDepth = 3;
            if (arguments.TryGetValue("maxDepth", out var depthObj))
            {
                maxDepth = depthObj is int i ? i : int.Parse(depthObj.ToString() ?? "3");
            }

            var sb = new StringBuilder();
            ListDirectory(fullPath, _config.BaseDirectory, sb, recursive, pattern, 0, maxDepth);

            _logger.LogDebug("Listed directory: {Path}", path);

            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = sb.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list directory: {Path}", path);

            return Task.FromResult(new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message
            });
        }
    }

    private void ListDirectory(string path, string basePath, StringBuilder sb, bool recursive, string pattern, int depth, int maxDepth)
    {
        var indent = new string(' ', depth * 2);
        var relativePath = Path.GetRelativePath(basePath, path);

        // List directories
        foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
        {
            var dirName = Path.GetFileName(dir);

            // Skip hidden and common ignored directories
            if (dirName.StartsWith('.') || dirName == "node_modules" || dirName == "bin" || dirName == "obj")
                continue;

            sb.AppendLine($"{indent}{dirName}/");

            if (recursive && depth < maxDepth)
            {
                ListDirectory(dir, basePath, sb, recursive, pattern, depth + 1, maxDepth);
            }
        }

        // List files
        foreach (var file in Directory.GetFiles(path, pattern).OrderBy(f => f))
        {
            var fileName = Path.GetFileName(file);

            // Skip hidden files
            if (fileName.StartsWith('.'))
                continue;

            var size = new FileInfo(file).Length;
            var sizeStr = FormatSize(size);
            sb.AppendLine($"{indent}{fileName} ({sizeStr})");
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private string? GetSecurePath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path, _config.BaseDirectory);

            if (!fullPath.StartsWith(_config.BaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
