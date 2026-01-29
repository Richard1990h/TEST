using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.State;

/// <summary>
/// Computes and applies file diffs.
/// </summary>
public interface IFileDiffEngine
{
    /// <summary>
    /// Compute diff between two strings.
    /// </summary>
    FileDiff ComputeDiff(string original, string modified);

    /// <summary>
    /// Apply a diff to content.
    /// </summary>
    string ApplyDiff(string original, FileDiff diff);

    /// <summary>
    /// Generate a unified diff string.
    /// </summary>
    string ToUnifiedDiff(FileDiff diff, string? fileName = null);

    /// <summary>
    /// Parse a unified diff string.
    /// </summary>
    FileDiff ParseUnifiedDiff(string unifiedDiff);
}

/// <summary>
/// Represents a file diff.
/// </summary>
public class FileDiff
{
    /// <summary>
    /// Original file content hash.
    /// </summary>
    public string? OriginalHash { get; set; }

    /// <summary>
    /// Modified file content hash.
    /// </summary>
    public string? ModifiedHash { get; set; }

    /// <summary>
    /// Hunks (chunks) of changes.
    /// </summary>
    public List<DiffHunk> Hunks { get; set; } = new();

    /// <summary>
    /// Whether there are any changes.
    /// </summary>
    public bool HasChanges => Hunks.Count > 0;

    /// <summary>
    /// Total lines added.
    /// </summary>
    public int LinesAdded => Hunks.Sum(h => h.Lines.Count(l => l.Type == DiffLineType.Added));

    /// <summary>
    /// Total lines removed.
    /// </summary>
    public int LinesRemoved => Hunks.Sum(h => h.Lines.Count(l => l.Type == DiffLineType.Removed));
}

/// <summary>
/// A hunk (section) of a diff.
/// </summary>
public class DiffHunk
{
    /// <summary>
    /// Starting line in original file.
    /// </summary>
    public int OriginalStart { get; set; }

    /// <summary>
    /// Number of lines from original.
    /// </summary>
    public int OriginalCount { get; set; }

    /// <summary>
    /// Starting line in modified file.
    /// </summary>
    public int ModifiedStart { get; set; }

    /// <summary>
    /// Number of lines in modified.
    /// </summary>
    public int ModifiedCount { get; set; }

    /// <summary>
    /// Lines in this hunk.
    /// </summary>
    public List<DiffLine> Lines { get; set; } = new();
}

/// <summary>
/// A line in a diff.
/// </summary>
public class DiffLine
{
    /// <summary>
    /// Type of change.
    /// </summary>
    public DiffLineType Type { get; set; }

    /// <summary>
    /// Content of the line.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Line number in original file (if applicable).
    /// </summary>
    public int? OriginalLineNumber { get; set; }

    /// <summary>
    /// Line number in modified file (if applicable).
    /// </summary>
    public int? ModifiedLineNumber { get; set; }
}

/// <summary>
/// Type of diff line.
/// </summary>
public enum DiffLineType
{
    Context,
    Added,
    Removed
}

/// <summary>
/// Implementation of file diff engine.
/// </summary>
public class FileDiffEngine : IFileDiffEngine
{
    private readonly ILogger<FileDiffEngine> _logger;
    private const int ContextLines = 3;

    public FileDiffEngine(ILogger<FileDiffEngine> logger)
    {
        _logger = logger;
    }

    public FileDiff ComputeDiff(string original, string modified)
    {
        var diff = new FileDiff();

        var originalLines = SplitLines(original);
        var modifiedLines = SplitLines(modified);

        // Compute LCS-based diff
        var lcs = ComputeLcs(originalLines, modifiedLines);
        var changes = ExtractChanges(originalLines, modifiedLines, lcs);

        // Group changes into hunks
        diff.Hunks = GroupIntoHunks(changes, originalLines, modifiedLines);

        _logger.LogDebug("Computed diff: {Added} added, {Removed} removed in {Hunks} hunks",
            diff.LinesAdded, diff.LinesRemoved, diff.Hunks.Count);

        return diff;
    }

    public string ApplyDiff(string original, FileDiff diff)
    {
        if (!diff.HasChanges)
            return original;

        var lines = SplitLines(original).ToList();

        // Apply hunks in reverse order to preserve line numbers
        foreach (var hunk in diff.Hunks.OrderByDescending(h => h.OriginalStart))
        {
            var insertIndex = hunk.OriginalStart - 1;
            var removeCount = hunk.Lines.Count(l => l.Type == DiffLineType.Removed || l.Type == DiffLineType.Context);

            // Remove old lines
            if (insertIndex >= 0 && insertIndex < lines.Count)
            {
                var toRemove = Math.Min(removeCount, lines.Count - insertIndex);
                lines.RemoveRange(insertIndex, toRemove);
            }

            // Insert new lines
            var newLines = hunk.Lines
                .Where(l => l.Type == DiffLineType.Added || l.Type == DiffLineType.Context)
                .Select(l => l.Content)
                .ToList();

            if (insertIndex >= 0)
            {
                lines.InsertRange(Math.Min(insertIndex, lines.Count), newLines);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string ToUnifiedDiff(FileDiff diff, string? fileName = null)
    {
        var lines = new List<string>();

        if (fileName != null)
        {
            lines.Add($"--- a/{fileName}");
            lines.Add($"+++ b/{fileName}");
        }

        foreach (var hunk in diff.Hunks)
        {
            lines.Add($"@@ -{hunk.OriginalStart},{hunk.OriginalCount} +{hunk.ModifiedStart},{hunk.ModifiedCount} @@");

            foreach (var line in hunk.Lines)
            {
                var prefix = line.Type switch
                {
                    DiffLineType.Added => "+",
                    DiffLineType.Removed => "-",
                    _ => " "
                };
                lines.Add($"{prefix}{line.Content}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public FileDiff ParseUnifiedDiff(string unifiedDiff)
    {
        var diff = new FileDiff();
        var lines = unifiedDiff.Split('\n');
        DiffHunk? currentHunk = null;

        var hunkHeaderRegex = new System.Text.RegularExpressions.Regex(
            @"^@@ -(\d+),?(\d*) \+(\d+),?(\d*) @@");

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                var match = hunkHeaderRegex.Match(line);
                if (match.Success)
                {
                    currentHunk = new DiffHunk
                    {
                        OriginalStart = int.Parse(match.Groups[1].Value),
                        OriginalCount = string.IsNullOrEmpty(match.Groups[2].Value) ? 1 : int.Parse(match.Groups[2].Value),
                        ModifiedStart = int.Parse(match.Groups[3].Value),
                        ModifiedCount = string.IsNullOrEmpty(match.Groups[4].Value) ? 1 : int.Parse(match.Groups[4].Value)
                    };
                    diff.Hunks.Add(currentHunk);
                }
            }
            else if (currentHunk != null && line.Length > 0)
            {
                var type = line[0] switch
                {
                    '+' => DiffLineType.Added,
                    '-' => DiffLineType.Removed,
                    _ => DiffLineType.Context
                };

                currentHunk.Lines.Add(new DiffLine
                {
                    Type = type,
                    Content = line.Length > 1 ? line.Substring(1) : ""
                });
            }
        }

        return diff;
    }

    private static string[] SplitLines(string text)
    {
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    private static int[,] ComputeLcs(string[] a, string[] b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                if (a[i - 1] == b[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }

        return dp;
    }

    private static List<(int origIndex, int modIndex, DiffLineType type)> ExtractChanges(
        string[] original, string[] modified, int[,] lcs)
    {
        var changes = new List<(int, int, DiffLineType)>();
        var i = original.Length;
        var j = modified.Length;

        while (i > 0 || j > 0)
        {
            if (i > 0 && j > 0 && original[i - 1] == modified[j - 1])
            {
                changes.Add((i - 1, j - 1, DiffLineType.Context));
                i--;
                j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                changes.Add((-1, j - 1, DiffLineType.Added));
                j--;
            }
            else if (i > 0)
            {
                changes.Add((i - 1, -1, DiffLineType.Removed));
                i--;
            }
        }

        changes.Reverse();
        return changes;
    }

    private List<DiffHunk> GroupIntoHunks(
        List<(int origIndex, int modIndex, DiffLineType type)> changes,
        string[] original, string[] modified)
    {
        var hunks = new List<DiffHunk>();

        if (!changes.Any(c => c.type != DiffLineType.Context))
            return hunks;

        DiffHunk? currentHunk = null;
        var contextBuffer = new List<(int origIndex, int modIndex, DiffLineType type)>();

        foreach (var change in changes)
        {
            if (change.type == DiffLineType.Context)
            {
                if (currentHunk != null)
                {
                    // Add trailing context
                    if (contextBuffer.Count <= ContextLines)
                    {
                        AddLinesToHunk(currentHunk, contextBuffer, original, modified);
                    }
                    else
                    {
                        // Start new hunk
                        AddLinesToHunk(currentHunk, contextBuffer.Take(ContextLines).ToList(), original, modified);
                        FinalizeHunk(currentHunk);
                        hunks.Add(currentHunk);
                        currentHunk = null;
                    }
                }

                contextBuffer.Clear();
                contextBuffer.Add(change);
            }
            else
            {
                if (currentHunk == null)
                {
                    currentHunk = new DiffHunk
                    {
                        OriginalStart = Math.Max(1, (change.origIndex >= 0 ? change.origIndex : 0) - ContextLines + 1),
                        ModifiedStart = Math.Max(1, (change.modIndex >= 0 ? change.modIndex : 0) - ContextLines + 1)
                    };

                    // Add leading context
                    var leadingContext = contextBuffer.TakeLast(ContextLines).ToList();
                    AddLinesToHunk(currentHunk, leadingContext, original, modified);
                }
                else
                {
                    AddLinesToHunk(currentHunk, contextBuffer, original, modified);
                }

                contextBuffer.Clear();

                var content = change.type == DiffLineType.Added
                    ? modified[change.modIndex]
                    : original[change.origIndex];

                currentHunk.Lines.Add(new DiffLine
                {
                    Type = change.type,
                    Content = content,
                    OriginalLineNumber = change.origIndex >= 0 ? change.origIndex + 1 : null,
                    ModifiedLineNumber = change.modIndex >= 0 ? change.modIndex + 1 : null
                });
            }
        }

        if (currentHunk != null)
        {
            AddLinesToHunk(currentHunk, contextBuffer.Take(ContextLines).ToList(), original, modified);
            FinalizeHunk(currentHunk);
            hunks.Add(currentHunk);
        }

        return hunks;
    }

    private static void AddLinesToHunk(
        DiffHunk hunk,
        List<(int origIndex, int modIndex, DiffLineType type)> changes,
        string[] original, string[] modified)
    {
        foreach (var change in changes)
        {
            var content = change.origIndex >= 0 && change.origIndex < original.Length
                ? original[change.origIndex]
                : (change.modIndex >= 0 && change.modIndex < modified.Length ? modified[change.modIndex] : "");

            hunk.Lines.Add(new DiffLine
            {
                Type = change.type,
                Content = content,
                OriginalLineNumber = change.origIndex >= 0 ? change.origIndex + 1 : null,
                ModifiedLineNumber = change.modIndex >= 0 ? change.modIndex + 1 : null
            });
        }
    }

    private static void FinalizeHunk(DiffHunk hunk)
    {
        hunk.OriginalCount = hunk.Lines.Count(l => l.Type != DiffLineType.Added);
        hunk.ModifiedCount = hunk.Lines.Count(l => l.Type != DiffLineType.Removed);
    }
}
