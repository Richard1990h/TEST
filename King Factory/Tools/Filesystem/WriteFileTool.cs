using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Tools.Filesystem;

/// <summary>
/// Tool for writing file contents.
/// </summary>
public class WriteFileTool : ITool
{
    private readonly ILogger<WriteFileTool> _logger;
    private readonly FilesystemConfig _config;
    private readonly IFileEventNotifier _notifier;

    public string Name => "write_file";
    public string Description => "Write content to a file. Creates the file if it doesn't exist, overwrites if it does.";
    public bool RequiresConfirmation => true; // Destructive operation

    public ToolSchema Schema => new()
    {
        Properties = new Dictionary<string, ToolParameter>
        {
            ["path"] = new()
            {
                Type = "string",
                Description = "Path to the file to write"
            },
            ["content"] = new()
            {
                Type = "string",
                Description = "Content to write to the file"
            },
            ["encoding"] = new()
            {
                Type = "string",
                Description = "File encoding (default: utf-8)",
                Default = "utf-8"
            },
            ["createDirectories"] = new()
            {
                Type = "boolean",
                Description = "Create parent directories if they don't exist",
                Default = true
            }
        },
        Required = new List<string> { "path", "content" }
    };

    public WriteFileTool(ILogger<WriteFileTool> logger, FilesystemConfig config, IFileEventNotifier notifier)
    {
        _logger = logger;
        _config = config;
        _notifier = notifier;
    }

    public ValidationResult ValidateArguments(Dictionary<string, object> arguments)
    {
        var errors = new List<string>();

        if (!arguments.TryGetValue("path", out var pathObj) || pathObj is not string path || string.IsNullOrWhiteSpace(path))
        {
            errors.Add("'path' is required");
        }
        else
        {
            var fullPath = GetSecurePath(path);
            if (fullPath == null)
            {
                errors.Add("Path is outside allowed directory");
            }
        }

        if (!arguments.ContainsKey("content"))
        {
            errors.Add("'content' is required");
        }

        return errors.Any() ? ValidationResult.Invalid(errors) : ValidationResult.Valid();
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var path = arguments["path"].ToString()!;
        var content = arguments["content"]?.ToString() ?? "";
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

        try
        {
            // Create directories if needed
            var createDirs = true;
            if (arguments.TryGetValue("createDirectories", out var createObj))
            {
                createDirs = createObj is bool b ? b : bool.Parse(createObj.ToString() ?? "true");
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && createDirs && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                await _notifier.NotifyDirectoryCreatedAsync(directory, cancellationToken);
            }

            var encoding = System.Text.Encoding.UTF8;
            if (arguments.TryGetValue("encoding", out var encObj) && encObj is string encName)
            {
                encoding = System.Text.Encoding.GetEncoding(encName);
            }

            var existed = File.Exists(fullPath);
            await File.WriteAllTextAsync(fullPath, content, encoding, cancellationToken);

            _logger.LogInformation("{Action} file: {Path} ({Length} chars)",
                existed ? "Updated" : "Created", path, content.Length);

            // Notify about the file change
            if (existed)
            {
                await _notifier.NotifyFileUpdatedAsync(fullPath, content, cancellationToken);
            }
            else
            {
                await _notifier.NotifyFileCreatedAsync(fullPath, content, cancellationToken);
            }

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = existed ? $"Updated: {path}" : $"Created: {path}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write file: {Path}", path);

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
