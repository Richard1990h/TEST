using LittleHelperAI.KingFactory.Tools.Filesystem;
using Microsoft.AspNetCore.Mvc;

namespace LittleHelperAI.Backend.Controllers;

/// <summary>
/// API controller for managing project files.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectFilesController : ControllerBase
{
    private readonly FilesystemConfig _config;
    private readonly ILogger<ProjectFilesController> _logger;

    public ProjectFilesController(FilesystemConfig config, ILogger<ProjectFilesController> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Get all files in the project directory as a tree structure.
    /// </summary>
    [HttpGet]
    public ActionResult<ProjectFilesResponse> GetProjectFiles()
    {
        try
        {
            var root = BuildFileTree(_config.BaseDirectory, "");
            return Ok(new ProjectFilesResponse
            {
                BaseDirectory = _config.BaseDirectory,
                Files = root
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list project files");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get file content by relative path.
    /// </summary>
    [HttpGet("content")]
    public async Task<ActionResult<FileContentResponse>> GetFileContent([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new { error = "Path is required" });
        }

        try
        {
            var fullPath = Path.GetFullPath(path, _config.BaseDirectory);

            // Security check
            if (!fullPath.StartsWith(_config.BaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Path is outside project directory" });
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = $"File not found: {path}" });
            }

            var content = await System.IO.File.ReadAllTextAsync(fullPath);
            return Ok(new FileContentResponse
            {
                Path = path,
                Content = content,
                LastModified = System.IO.File.GetLastWriteTimeUtc(fullPath)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file: {Path}", path);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get project directory info.
    /// </summary>
    [HttpGet("info")]
    public ActionResult<ProjectInfoResponse> GetProjectInfo()
    {
        return Ok(new ProjectInfoResponse
        {
            BaseDirectory = _config.BaseDirectory,
            Exists = Directory.Exists(_config.BaseDirectory),
            TotalFiles = Directory.Exists(_config.BaseDirectory)
                ? Directory.GetFiles(_config.BaseDirectory, "*", SearchOption.AllDirectories).Length
                : 0
        });
    }

    private List<FileTreeNode> BuildFileTree(string path, string relativePath)
    {
        var result = new List<FileTreeNode>();

        if (!Directory.Exists(path))
            return result;

        // Add directories
        foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
        {
            var dirName = Path.GetFileName(dir);

            // Skip hidden and common ignored directories
            if (dirName.StartsWith('.') || dirName == "node_modules" || dirName == "bin" || dirName == "obj")
                continue;

            var childRelativePath = string.IsNullOrEmpty(relativePath) ? dirName : $"{relativePath}/{dirName}";
            result.Add(new FileTreeNode
            {
                Name = dirName,
                Path = childRelativePath,
                IsDirectory = true,
                Children = BuildFileTree(dir, childRelativePath)
            });
        }

        // Add files
        foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
        {
            var fileName = Path.GetFileName(file);

            // Skip hidden files
            if (fileName.StartsWith('.'))
                continue;

            var fileRelativePath = string.IsNullOrEmpty(relativePath) ? fileName : $"{relativePath}/{fileName}";
            var fileInfo = new FileInfo(file);

            result.Add(new FileTreeNode
            {
                Name = fileName,
                Path = fileRelativePath,
                IsDirectory = false,
                Size = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc
            });
        }

        return result;
    }
}

// DTOs
public class ProjectFilesResponse
{
    public string BaseDirectory { get; set; } = string.Empty;
    public List<FileTreeNode> Files { get; set; } = new();
}

public class FileTreeNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public List<FileTreeNode>? Children { get; set; }
}

public class FileContentResponse
{
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class ProjectInfoResponse
{
    public string BaseDirectory { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public int TotalFiles { get; set; }
}
