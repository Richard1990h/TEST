using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Tools.Filesystem;

/// <summary>
/// Tool for reading file contents.
/// </summary>
public class ReadFileTool : ITool
{
    private readonly ILogger<ReadFileTool> _logger;
    private readonly FilesystemConfig _config;

    public string Name => "read_file";
    public string Description => "Read the contents of a file at the specified path.";
    public bool RequiresConfirmation => false;

    public ToolSchema Schema => new()
    {
        Properties = new Dictionary<string, ToolParameter>
        {
            ["path"] = new()
            {
                Type = "string",
                Description = "Path to the file to read"
            },
            ["encoding"] = new()
            {
                Type = "string",
                Description = "File encoding (default: utf-8)",
                Default = "utf-8"
            }
        },
        Required = new List<string> { "path" }
    };

    public ReadFileTool(ILogger<ReadFileTool> logger, FilesystemConfig config)
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

        // Security: Ensure path is within allowed directory
        var fullPath = GetSecurePath(path);
        if (fullPath == null)
        {
            return ValidationResult.Invalid("Path is outside allowed directory");
        }

        return ValidationResult.Valid();
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var path = arguments["path"].ToString()!;
        var fullPath = GetSecurePath(path);

        if (fullPath == null)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = "Path is outside allowed directory"
            };
        }

        if (!File.Exists(fullPath))
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = $"File not found: {path}"
            };
        }

        try
        {
            var encoding = System.Text.Encoding.UTF8;
            if (arguments.TryGetValue("encoding", out var encObj) && encObj is string encName)
            {
                encoding = System.Text.Encoding.GetEncoding(encName);
            }

            var content = await File.ReadAllTextAsync(fullPath, encoding, cancellationToken);

            _logger.LogDebug("Read file: {Path} ({Length} chars)", path, content.Length);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file: {Path}", path);

            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private string? GetSecurePath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path, _config.BaseDirectory);

            // Ensure path is within base directory
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
