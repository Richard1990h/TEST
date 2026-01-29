using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Intent;

/// <summary>
/// Detects build/compile/run signals in user messages.
/// </summary>
public interface IBuildSignalDetector
{
    /// <summary>
    /// Detect if the message contains a build signal.
    /// </summary>
    BuildSignal Detect(string message);

    /// <summary>
    /// Get the suggested build command for a project type.
    /// </summary>
    string? GetBuildCommand(string projectType);
}

/// <summary>
/// Result of build signal detection.
/// </summary>
public class BuildSignal
{
    /// <summary>
    /// Whether a build signal was detected.
    /// </summary>
    public bool Detected { get; set; }

    /// <summary>
    /// Type of build operation detected.
    /// </summary>
    public BuildOperation Operation { get; set; }

    /// <summary>
    /// Confidence in the detection.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Detected project type if any.
    /// </summary>
    public string? ProjectType { get; set; }

    /// <summary>
    /// Suggested command to run.
    /// </summary>
    public string? SuggestedCommand { get; set; }

    /// <summary>
    /// Additional arguments extracted.
    /// </summary>
    public Dictionary<string, string> Arguments { get; set; } = new();
}

/// <summary>
/// Types of build operations.
/// </summary>
public enum BuildOperation
{
    None,
    Build,
    Rebuild,
    Clean,
    Run,
    Test,
    Package,
    Deploy,
    Watch
}

/// <summary>
/// Detects build-related intents in messages.
/// </summary>
public class BuildSignalDetector : IBuildSignalDetector
{
    private readonly ILogger<BuildSignalDetector> _logger;

    private static readonly Dictionary<string, BuildOperation> BuildKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        { "build", BuildOperation.Build },
        { "compile", BuildOperation.Build },
        { "rebuild", BuildOperation.Rebuild },
        { "clean", BuildOperation.Clean },
        { "run", BuildOperation.Run },
        { "start", BuildOperation.Run },
        { "execute", BuildOperation.Run },
        { "test", BuildOperation.Test },
        { "package", BuildOperation.Package },
        { "publish", BuildOperation.Package },
        { "deploy", BuildOperation.Deploy },
        { "watch", BuildOperation.Watch }
    };

    private static readonly Dictionary<string, string[]> ProjectIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        { "dotnet", new[] { "dotnet", ".csproj", ".sln", "c#", "csharp" } },
        { "node", new[] { "npm", "node", "yarn", "package.json", "javascript", "typescript" } },
        { "python", new[] { "python", "pip", "requirements.txt", ".py" } },
        { "rust", new[] { "cargo", "rust", ".rs", "Cargo.toml" } },
        { "go", new[] { "go", "golang", "go.mod" } },
        { "java", new[] { "maven", "gradle", "java", ".java", "pom.xml" } }
    };

    private static readonly Dictionary<string, Dictionary<BuildOperation, string>> BuildCommands = new()
    {
        ["dotnet"] = new()
        {
            { BuildOperation.Build, "dotnet build" },
            { BuildOperation.Rebuild, "dotnet build --no-incremental" },
            { BuildOperation.Clean, "dotnet clean" },
            { BuildOperation.Run, "dotnet run" },
            { BuildOperation.Test, "dotnet test" },
            { BuildOperation.Package, "dotnet publish" },
            { BuildOperation.Watch, "dotnet watch run" }
        },
        ["node"] = new()
        {
            { BuildOperation.Build, "npm run build" },
            { BuildOperation.Clean, "npm run clean" },
            { BuildOperation.Run, "npm start" },
            { BuildOperation.Test, "npm test" },
            { BuildOperation.Watch, "npm run dev" }
        },
        ["python"] = new()
        {
            { BuildOperation.Run, "python main.py" },
            { BuildOperation.Test, "pytest" }
        },
        ["rust"] = new()
        {
            { BuildOperation.Build, "cargo build" },
            { BuildOperation.Rebuild, "cargo build --release" },
            { BuildOperation.Clean, "cargo clean" },
            { BuildOperation.Run, "cargo run" },
            { BuildOperation.Test, "cargo test" }
        },
        ["go"] = new()
        {
            { BuildOperation.Build, "go build" },
            { BuildOperation.Run, "go run ." },
            { BuildOperation.Test, "go test ./..." }
        },
        ["java"] = new()
        {
            { BuildOperation.Build, "mvn compile" },
            { BuildOperation.Clean, "mvn clean" },
            { BuildOperation.Run, "mvn exec:java" },
            { BuildOperation.Test, "mvn test" },
            { BuildOperation.Package, "mvn package" }
        }
    };

    public BuildSignalDetector(ILogger<BuildSignalDetector> logger)
    {
        _logger = logger;
    }

    public BuildSignal Detect(string message)
    {
        var normalizedMessage = message.ToLowerInvariant();
        var result = new BuildSignal();

        // Detect operation
        foreach (var keyword in BuildKeywords)
        {
            if (ContainsWord(normalizedMessage, keyword.Key))
            {
                result.Detected = true;
                result.Operation = keyword.Value;
                result.Confidence = 0.7;
                break;
            }
        }

        if (!result.Detected)
        {
            return result;
        }

        // Detect project type
        foreach (var project in ProjectIndicators)
        {
            if (project.Value.Any(indicator => normalizedMessage.Contains(indicator)))
            {
                result.ProjectType = project.Key;
                result.Confidence += 0.2;
                break;
            }
        }

        // Get suggested command
        if (result.ProjectType != null && BuildCommands.TryGetValue(result.ProjectType, out var commands))
        {
            if (commands.TryGetValue(result.Operation, out var command))
            {
                result.SuggestedCommand = command;
            }
        }

        // Extract configuration arguments
        if (normalizedMessage.Contains("release"))
        {
            result.Arguments["configuration"] = "Release";
        }
        else if (normalizedMessage.Contains("debug"))
        {
            result.Arguments["configuration"] = "Debug";
        }

        _logger.LogDebug("Build signal detected: {Operation} for {ProjectType} (confidence: {Confidence})",
            result.Operation, result.ProjectType ?? "unknown", result.Confidence);

        return result;
    }

    public string? GetBuildCommand(string projectType)
    {
        if (BuildCommands.TryGetValue(projectType, out var commands))
        {
            return commands.GetValueOrDefault(BuildOperation.Build);
        }
        return null;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;

        // Check word boundaries
        var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        var after = index + word.Length >= text.Length || !char.IsLetterOrDigit(text[index + word.Length]);

        return before && after;
    }
}
