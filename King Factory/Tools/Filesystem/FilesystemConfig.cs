namespace LittleHelperAI.KingFactory.Tools.Filesystem;

/// <summary>
/// Configuration for filesystem tools.
/// </summary>
public class FilesystemConfig
{
    /// <summary>
    /// Base directory for all file operations. Tools cannot access paths outside this directory.
    /// </summary>
    public string BaseDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>
    /// Maximum file size that can be read (in bytes).
    /// </summary>
    public long MaxReadSize { get; set; } = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Maximum file size that can be written (in bytes).
    /// </summary>
    public long MaxWriteSize { get; set; } = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// File extensions that are blocked from reading.
    /// </summary>
    public HashSet<string> BlockedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib",
        ".key", ".pem", ".pfx", ".p12",
        ".env"
    };

    /// <summary>
    /// Directories that are blocked from access.
    /// </summary>
    public HashSet<string> BlockedDirectories { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".ssh",
        "node_modules"
    };

    /// <summary>
    /// Check if a file extension is blocked.
    /// </summary>
    public bool IsExtensionBlocked(string path)
    {
        var ext = Path.GetExtension(path);
        return BlockedExtensions.Contains(ext);
    }

    /// <summary>
    /// Check if a directory is blocked.
    /// </summary>
    public bool IsDirectoryBlocked(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => BlockedDirectories.Contains(p));
    }
}
