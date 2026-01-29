using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace LittleHelperAI.KingFactory.Intent;

/// <summary>
/// Extracts the scope of user requests (files, functions, classes, etc.).
/// </summary>
public interface IScopeExtractor
{
    /// <summary>
    /// Extract scope information from a message.
    /// </summary>
    ScopeResult Extract(string message);
}

/// <summary>
/// Result of scope extraction.
/// </summary>
public class ScopeResult
{
    /// <summary>
    /// Type of scope detected.
    /// </summary>
    public ScopeType Type { get; set; } = ScopeType.Unknown;

    /// <summary>
    /// Primary target (e.g., file path, function name).
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Secondary targets if multiple found.
    /// </summary>
    public List<string> AdditionalTargets { get; set; } = new();

    /// <summary>
    /// File paths mentioned.
    /// </summary>
    public List<string> FilePaths { get; set; } = new();

    /// <summary>
    /// Function/method names mentioned.
    /// </summary>
    public List<string> FunctionNames { get; set; } = new();

    /// <summary>
    /// Class/type names mentioned.
    /// </summary>
    public List<string> ClassNames { get; set; } = new();

    /// <summary>
    /// Line numbers or ranges mentioned.
    /// </summary>
    public List<LineRange> LineRanges { get; set; } = new();

    /// <summary>
    /// Directory paths mentioned.
    /// </summary>
    public List<string> Directories { get; set; } = new();

    /// <summary>
    /// Whether the scope is the entire project.
    /// </summary>
    public bool IsProjectWide { get; set; }

    /// <summary>
    /// Confidence in the extraction.
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Types of scope.
/// </summary>
public enum ScopeType
{
    Unknown,
    File,
    Function,
    Class,
    LineRange,
    Directory,
    Project
}

/// <summary>
/// Represents a range of lines.
/// </summary>
public class LineRange
{
    public int Start { get; set; }
    public int End { get; set; }

    public LineRange(int start, int end)
    {
        Start = start;
        End = end;
    }

    public LineRange(int line) : this(line, line) { }
}

/// <summary>
/// Extracts scope information from user messages.
/// </summary>
public class ScopeExtractor : IScopeExtractor
{
    private readonly ILogger<ScopeExtractor> _logger;

    // Regex patterns
    private static readonly Regex FilePathPattern = new(
        @"(?:^|[\s""'])([A-Za-z]:[/\\])?(?:[\w.-]+[/\\])*[\w.-]+\.(cs|js|ts|py|go|rs|java|cpp|c|h|json|yaml|yml|xml|md|txt|html|css|scss|vue|jsx|tsx)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FunctionPattern = new(
        @"(?:function|method|def|func|fn)\s+[`']?(\w+)[`']?|(\w+)\s*\(|`(\w+)`\s*(?:function|method)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ClassPattern = new(
        @"(?:class|interface|struct|type|enum)\s+[`']?(\w+)[`']?|[`'](\w+)[`']\s*(?:class|interface|struct)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LineNumberPattern = new(
        @"line\s*(\d+)(?:\s*(?:to|-)\s*(\d+))?|lines?\s*(\d+)\s*(?:(?:to|-|and)\s*(\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DirectoryPattern = new(
        @"(?:folder|directory|dir|path)\s+[`'""]?([/\\]?[\w.-]+(?:[/\\][\w.-]+)*)[`'""]?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ProjectWideKeywords = {
        "entire project", "whole project", "all files", "entire codebase",
        "whole codebase", "project-wide", "everywhere", "across the project"
    };

    public ScopeExtractor(ILogger<ScopeExtractor> logger)
    {
        _logger = logger;
    }

    public ScopeResult Extract(string message)
    {
        var result = new ScopeResult();
        var normalizedMessage = message.ToLowerInvariant();

        // Check for project-wide scope
        if (ProjectWideKeywords.Any(k => normalizedMessage.Contains(k)))
        {
            result.IsProjectWide = true;
            result.Type = ScopeType.Project;
            result.Confidence = 0.9;
        }

        // Extract file paths
        var fileMatches = FilePathPattern.Matches(message);
        foreach (Match match in fileMatches)
        {
            var path = match.Value.Trim('\'', '"', ' ');
            if (!string.IsNullOrWhiteSpace(path))
            {
                result.FilePaths.Add(path);
            }
        }

        // Extract function names
        var funcMatches = FunctionPattern.Matches(message);
        foreach (Match match in funcMatches)
        {
            var name = match.Groups.Cast<Group>()
                .Skip(1)
                .FirstOrDefault(g => g.Success)?.Value;

            if (!string.IsNullOrWhiteSpace(name) && !IsCommonWord(name))
            {
                result.FunctionNames.Add(name);
            }
        }

        // Extract class names
        var classMatches = ClassPattern.Matches(message);
        foreach (Match match in classMatches)
        {
            var name = match.Groups.Cast<Group>()
                .Skip(1)
                .FirstOrDefault(g => g.Success)?.Value;

            if (!string.IsNullOrWhiteSpace(name) && !IsCommonWord(name))
            {
                result.ClassNames.Add(name);
            }
        }

        // Extract line numbers
        var lineMatches = LineNumberPattern.Matches(message);
        foreach (Match match in lineMatches)
        {
            var groups = match.Groups.Cast<Group>().Where(g => g.Success).Skip(1).ToList();
            if (groups.Count >= 1 && int.TryParse(groups[0].Value, out var start))
            {
                var end = start;
                if (groups.Count >= 2 && int.TryParse(groups[1].Value, out var endLine))
                {
                    end = endLine;
                }
                result.LineRanges.Add(new LineRange(start, end));
            }
        }

        // Extract directories
        var dirMatches = DirectoryPattern.Matches(message);
        foreach (Match match in dirMatches)
        {
            if (match.Groups[1].Success)
            {
                result.Directories.Add(match.Groups[1].Value);
            }
        }

        // Determine primary scope type and target
        DeterminePrimaryScope(result);

        _logger.LogDebug("Extracted scope: {Type} targeting {Target} (confidence: {Confidence})",
            result.Type, result.Target ?? "none", result.Confidence);

        return result;
    }

    private void DeterminePrimaryScope(ScopeResult result)
    {
        if (result.IsProjectWide)
        {
            return; // Already set
        }

        // Priority: File > Class > Function > LineRange > Directory
        if (result.FilePaths.Count > 0)
        {
            result.Type = ScopeType.File;
            result.Target = result.FilePaths[0];
            result.AdditionalTargets = result.FilePaths.Skip(1).ToList();
            result.Confidence = 0.85;
        }
        else if (result.ClassNames.Count > 0)
        {
            result.Type = ScopeType.Class;
            result.Target = result.ClassNames[0];
            result.AdditionalTargets = result.ClassNames.Skip(1).ToList();
            result.Confidence = 0.75;
        }
        else if (result.FunctionNames.Count > 0)
        {
            result.Type = ScopeType.Function;
            result.Target = result.FunctionNames[0];
            result.AdditionalTargets = result.FunctionNames.Skip(1).ToList();
            result.Confidence = 0.7;
        }
        else if (result.LineRanges.Count > 0)
        {
            result.Type = ScopeType.LineRange;
            result.Confidence = 0.65;
        }
        else if (result.Directories.Count > 0)
        {
            result.Type = ScopeType.Directory;
            result.Target = result.Directories[0];
            result.AdditionalTargets = result.Directories.Skip(1).ToList();
            result.Confidence = 0.6;
        }
        else
        {
            result.Type = ScopeType.Unknown;
            result.Confidence = 0.3;
        }
    }

    private static bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "this", "that", "it", "is", "are", "was", "were",
            "be", "been", "being", "have", "has", "had", "do", "does", "did",
            "will", "would", "could", "should", "may", "might", "must", "can",
            "if", "then", "else", "when", "where", "how", "what", "why", "which",
            "file", "line", "code", "function", "method", "class", "type"
        };

        return commonWords.Contains(word);
    }
}
