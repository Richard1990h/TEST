namespace LittleHelperAI.KingFactory.Tools.Shell;

/// <summary>
/// Configuration for shell command tools.
/// </summary>
public class ShellConfig
{
    /// <summary>
    /// Default timeout for commands in seconds.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum timeout allowed in seconds.
    /// </summary>
    public int MaxTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Working directory for commands.
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>
    /// Shell to use on Windows.
    /// </summary>
    public string WindowsShell { get; set; } = "powershell.exe";

    /// <summary>
    /// Shell arguments on Windows.
    /// </summary>
    public string WindowsShellArgs { get; set; } = "-NoProfile -NonInteractive -Command";

    /// <summary>
    /// Shell to use on Unix.
    /// </summary>
    public string UnixShell { get; set; } = "/bin/bash";

    /// <summary>
    /// Shell arguments on Unix.
    /// </summary>
    public string UnixShellArgs { get; set; } = "-c";

    /// <summary>
    /// Commands that are blocked from execution.
    /// </summary>
    public HashSet<string> BlockedCommands { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Dangerous commands
        "rm -rf /",
        "rm -rf /*",
        "format",
        "mkfs",
        "dd if=/dev/zero",
        ":(){:|:&};:",
        // Network attacks
        "nc -l",
        "ncat",
        // Privilege escalation
        "sudo su",
        "sudo -i",
        // Registry manipulation
        "reg delete",
        "regedit"
    };

    /// <summary>
    /// Command prefixes that are blocked.
    /// </summary>
    public HashSet<string> BlockedPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "sudo rm -rf",
        "chmod 777",
        "curl | bash",
        "wget | bash"
    };

    /// <summary>
    /// Check if a command is blocked.
    /// </summary>
    public bool IsCommandBlocked(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();

        // Check exact matches
        if (BlockedCommands.Any(bc => normalized.Contains(bc.ToLowerInvariant())))
            return true;

        // Check prefixes
        if (BlockedPrefixes.Any(bp => normalized.StartsWith(bp.ToLowerInvariant())))
            return true;

        return false;
    }
}
