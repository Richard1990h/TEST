using System.Collections.Concurrent;

namespace LittleHelperAI.KingFactory.State;

/// <summary>
/// Manages project state and context.
/// </summary>
public interface IProjectState
{
    /// <summary>
    /// Get the current working directory.
    /// </summary>
    string WorkingDirectory { get; }

    /// <summary>
    /// Set the working directory.
    /// </summary>
    void SetWorkingDirectory(string path);

    /// <summary>
    /// Get tracked files.
    /// </summary>
    IReadOnlyCollection<TrackedFile> GetTrackedFiles();

    /// <summary>
    /// Track a file.
    /// </summary>
    void TrackFile(string path);

    /// <summary>
    /// Untrack a file.
    /// </summary>
    void UntrackFile(string path);

    /// <summary>
    /// Get file modifications.
    /// </summary>
    IReadOnlyList<FileModification> GetModifications();

    /// <summary>
    /// Record a file modification.
    /// </summary>
    void RecordModification(FileModification modification);

    /// <summary>
    /// Get project metadata.
    /// </summary>
    ProjectMetadata GetMetadata();

    /// <summary>
    /// Update project metadata.
    /// </summary>
    void UpdateMetadata(Action<ProjectMetadata> update);

    /// <summary>
    /// Clear all state.
    /// </summary>
    void Clear();
}

/// <summary>
/// Represents a tracked file.
/// </summary>
public class TrackedFile
{
    public string Path { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public DateTime LastModified { get; set; }
    public long Size { get; set; }
    public bool IsModified { get; set; }
}

/// <summary>
/// Represents a file modification.
/// </summary>
public class FileModification
{
    public string Path { get; set; } = string.Empty;
    public ModificationType Type { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
    public bool CanUndo { get; set; } = true;
}

/// <summary>
/// Types of file modifications.
/// </summary>
public enum ModificationType
{
    Created,
    Modified,
    Deleted,
    Renamed,
    Moved
}

/// <summary>
/// Project metadata.
/// </summary>
public class ProjectMetadata
{
    public string? Name { get; set; }
    public string? Language { get; set; }
    public string? Framework { get; set; }
    public List<string> EntryPoints { get; set; } = new();
    public Dictionary<string, string> Properties { get; set; } = new();
    public DateTime? DetectedAt { get; set; }
}

/// <summary>
/// In-memory project state implementation.
/// </summary>
public class ProjectState : IProjectState
{
    private string _workingDirectory;
    private readonly ConcurrentDictionary<string, TrackedFile> _trackedFiles = new();
    private readonly List<FileModification> _modifications = new();
    private readonly object _modLock = new();
    private ProjectMetadata _metadata = new();

    public ProjectState(string? initialDirectory = null)
    {
        _workingDirectory = initialDirectory ?? Environment.CurrentDirectory;
    }

    public string WorkingDirectory => _workingDirectory;

    public void SetWorkingDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            _workingDirectory = Path.GetFullPath(path);
        }
        else
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
    }

    public IReadOnlyCollection<TrackedFile> GetTrackedFiles()
    {
        return _trackedFiles.Values.ToList().AsReadOnly();
    }

    public void TrackFile(string path)
    {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
            return;

        var fileInfo = new FileInfo(fullPath);
        var tracked = new TrackedFile
        {
            Path = fullPath,
            LastModified = fileInfo.LastWriteTimeUtc,
            Size = fileInfo.Length,
            ContentHash = ComputeHash(fullPath)
        };

        _trackedFiles.AddOrUpdate(fullPath, tracked, (_, _) => tracked);
    }

    public void UntrackFile(string path)
    {
        var fullPath = GetFullPath(path);
        _trackedFiles.TryRemove(fullPath, out _);
    }

    public IReadOnlyList<FileModification> GetModifications()
    {
        lock (_modLock)
        {
            return _modifications.ToList().AsReadOnly();
        }
    }

    public void RecordModification(FileModification modification)
    {
        lock (_modLock)
        {
            modification.Path = GetFullPath(modification.Path);
            _modifications.Add(modification);

            // Update tracked file if exists
            if (_trackedFiles.TryGetValue(modification.Path, out var tracked))
            {
                tracked.IsModified = true;
                tracked.LastModified = DateTime.UtcNow;

                if (modification.Type == ModificationType.Modified && File.Exists(modification.Path))
                {
                    tracked.ContentHash = ComputeHash(modification.Path);
                    tracked.Size = new FileInfo(modification.Path).Length;
                }
            }
        }
    }

    public ProjectMetadata GetMetadata()
    {
        return _metadata;
    }

    public void UpdateMetadata(Action<ProjectMetadata> update)
    {
        update(_metadata);
        _metadata.DetectedAt ??= DateTime.UtcNow;
    }

    public void Clear()
    {
        _trackedFiles.Clear();
        lock (_modLock)
        {
            _modifications.Clear();
        }
        _metadata = new ProjectMetadata();
    }

    private string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(_workingDirectory, path));
    }

    private static string? ComputeHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return Convert.ToBase64String(hash);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Detects project type and metadata.
/// </summary>
public interface IProjectDetector
{
    /// <summary>
    /// Detect project metadata from a directory.
    /// </summary>
    Task<ProjectMetadata> DetectAsync(string directory, CancellationToken cancellationToken = default);
}

/// <summary>
/// File-based project detection.
/// </summary>
public class ProjectDetector : IProjectDetector
{
    private static readonly Dictionary<string, (string Language, string Framework)> ProjectFiles = new()
    {
        { "*.csproj", ("C#", ".NET") },
        { "*.sln", ("C#", ".NET") },
        { "package.json", ("JavaScript", "Node.js") },
        { "tsconfig.json", ("TypeScript", "Node.js") },
        { "requirements.txt", ("Python", "Python") },
        { "pyproject.toml", ("Python", "Python") },
        { "Cargo.toml", ("Rust", "Cargo") },
        { "go.mod", ("Go", "Go") },
        { "pom.xml", ("Java", "Maven") },
        { "build.gradle", ("Java", "Gradle") },
        { "Gemfile", ("Ruby", "Ruby") },
        { "composer.json", ("PHP", "Composer") },
    };

    public Task<ProjectMetadata> DetectAsync(string directory, CancellationToken cancellationToken = default)
    {
        var metadata = new ProjectMetadata
        {
            DetectedAt = DateTime.UtcNow
        };

        if (!Directory.Exists(directory))
        {
            return Task.FromResult(metadata);
        }

        // Check for project files
        foreach (var (pattern, info) in ProjectFiles)
        {
            var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length > 0)
            {
                metadata.Language = info.Language;
                metadata.Framework = info.Framework;

                // Try to extract project name
                var projectFile = files[0];
                metadata.Name = Path.GetFileNameWithoutExtension(projectFile);

                // Find entry points
                metadata.EntryPoints = FindEntryPoints(directory, info.Language);

                break;
            }
        }

        // Fallback: try to detect from file extensions
        if (metadata.Language == null)
        {
            metadata.Language = DetectLanguageFromExtensions(directory);
        }

        return Task.FromResult(metadata);
    }

    private static string? DetectLanguageFromExtensions(string directory)
    {
        var extensionCounts = new Dictionary<string, int>();

        try
        {
            var files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext))
                {
                    extensionCounts.TryGetValue(ext, out var count);
                    extensionCounts[ext] = count + 1;
                }
            }
        }
        catch
        {
            return null;
        }

        var extensionToLanguage = new Dictionary<string, string>
        {
            { ".cs", "C#" },
            { ".js", "JavaScript" },
            { ".ts", "TypeScript" },
            { ".py", "Python" },
            { ".rs", "Rust" },
            { ".go", "Go" },
            { ".java", "Java" },
            { ".rb", "Ruby" },
            { ".php", "PHP" },
            { ".cpp", "C++" },
            { ".c", "C" },
        };

        var topExt = extensionCounts
            .Where(kv => extensionToLanguage.ContainsKey(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault();

        return topExt.Key != null ? extensionToLanguage.GetValueOrDefault(topExt.Key) : null;
    }

    private static List<string> FindEntryPoints(string directory, string language)
    {
        var entryPoints = new List<string>();

        var patterns = language switch
        {
            "C#" => new[] { "Program.cs", "Startup.cs" },
            "JavaScript" or "TypeScript" => new[] { "index.js", "index.ts", "main.js", "main.ts", "app.js", "app.ts" },
            "Python" => new[] { "main.py", "app.py", "__main__.py" },
            "Rust" => new[] { "main.rs", "lib.rs" },
            "Go" => new[] { "main.go" },
            "Java" => new[] { "Main.java", "App.java", "Application.java" },
            _ => Array.Empty<string>()
        };

        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
            entryPoints.AddRange(files.Take(3));
        }

        return entryPoints;
    }
}
