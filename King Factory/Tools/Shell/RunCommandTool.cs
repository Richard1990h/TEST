using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LittleHelperAI.KingFactory.Tools.Shell;

/// <summary>
/// Tool for executing shell commands.
/// </summary>
public class RunCommandTool : ITool
{
    private readonly ILogger<RunCommandTool> _logger;
    private readonly ShellConfig _config;

    public string Name => "run_command";
    public string Description => "Execute a shell command and return the output.";
    public bool RequiresConfirmation => true; // Shell commands are potentially dangerous

    public ToolSchema Schema => new()
    {
        Properties = new Dictionary<string, ToolParameter>
        {
            ["command"] = new()
            {
                Type = "string",
                Description = "The command to execute"
            },
            ["workingDirectory"] = new()
            {
                Type = "string",
                Description = "Working directory for the command (optional)"
            },
            ["timeout"] = new()
            {
                Type = "integer",
                Description = "Timeout in seconds (default: 30, max: 300)"
            }
        },
        Required = new List<string> { "command" }
    };

    public RunCommandTool(ILogger<RunCommandTool> logger, ShellConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public ValidationResult ValidateArguments(Dictionary<string, object> arguments)
    {
        if (!arguments.TryGetValue("command", out var cmdObj) || cmdObj is not string command || string.IsNullOrWhiteSpace(command))
        {
            return ValidationResult.Invalid("'command' is required");
        }

        if (_config.IsCommandBlocked(command))
        {
            return ValidationResult.Invalid("This command is blocked for security reasons");
        }

        return ValidationResult.Valid();
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var command = arguments["command"].ToString()!;

        // Security check
        if (_config.IsCommandBlocked(command))
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = "Command blocked for security reasons"
            };
        }

        var workingDir = _config.WorkingDirectory;
        if (arguments.TryGetValue("workingDirectory", out var wdObj) && wdObj is string wd && !string.IsNullOrWhiteSpace(wd))
        {
            workingDir = Path.GetFullPath(wd, _config.WorkingDirectory);
        }

        var timeout = _config.DefaultTimeoutSeconds;
        if (arguments.TryGetValue("timeout", out var toObj))
        {
            timeout = toObj is int t ? t : int.Parse(toObj.ToString() ?? _config.DefaultTimeoutSeconds.ToString());
            timeout = Math.Min(timeout, _config.MaxTimeoutSeconds);
        }

        _logger.LogInformation("Executing command: {Command} in {WorkingDir}", command, workingDir);

        try
        {
            var (shell, shellArgs) = GetShellInfo();

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"{shellArgs} \"{EscapeCommand(command)}\"",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) stdout.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) stderr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = $"Command timed out after {timeout} seconds"
                };
            }

            var output = stdout.ToString().Trim();
            var error = stderr.ToString().Trim();
            var exitCode = process.ExitCode;

            _logger.LogInformation("Command completed with exit code {ExitCode}", exitCode);

            var result = new StringBuilder();
            if (!string.IsNullOrEmpty(output))
            {
                result.AppendLine(output);
            }
            if (!string.IsNullOrEmpty(error))
            {
                result.AppendLine($"[stderr]: {error}");
            }
            result.AppendLine($"[exit code]: {exitCode}");

            return new ToolResult
            {
                ToolName = Name,
                Success = exitCode == 0,
                Output = result.ToString().Trim(),
                Error = exitCode != 0 ? $"Command exited with code {exitCode}" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command: {Command}", command);

            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private (string shell, string args) GetShellInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return (_config.WindowsShell, _config.WindowsShellArgs);
        }

        return (_config.UnixShell, _config.UnixShellArgs);
    }

    private static string EscapeCommand(string command)
    {
        // Basic escaping for shell execution
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // PowerShell escaping
            return command.Replace("\"", "`\"");
        }

        // Bash escaping
        return command.Replace("\"", "\\\"");
    }
}
