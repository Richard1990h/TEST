using System.Text;

namespace LittleHelperAI.Dashboard.Pages;

/// <summary>
/// Local model for code analysis results (replaces old CodeAnalysis project dependency).
/// </summary>
public class CodeAnalysisResult
{
    public List<string>? Errors { get; set; }
    public List<string>? Warnings { get; set; }
    public string? OriginalCode { get; set; }
    public string? FixedCode { get; set; }
}

public partial class ChatArea
{
    // ==========================================
    // NO GAPS - ONLY BORDER LINES SEPARATE
    // ==========================================
    private string BuildAnalysisResponse(CodeAnalysisResult result, string fileName)
    {
        if (result == null)
            return "<p>No analysis available</p>";

        var html = new StringBuilder();
        
        var errorCount = result.Errors?.Count ?? 0;
        var warningCount = result.Warnings?.Count ?? 0;
        var totalIssues = errorCount + warningCount;
        
        // Check if there are actual differences between original and fixed
        bool hasActualFix = !string.IsNullOrEmpty(result.FixedCode) && 
                           !string.IsNullOrEmpty(result.OriginalCode) &&
                           !string.Equals(result.OriginalCode, result.FixedCode, StringComparison.Ordinal);

        // START SINGLE CONTAINER
        html.Append(@"<div style='display: flex; flex-direction: column; gap: 0; margin: 0; padding: 0;'>");

        // ===== HEADER WITH STATS =====
        html.Append($@"<div style='padding: 0.3rem 0.5rem; border-bottom: 1px solid var(--border-color); margin: 0;'>
<div style='display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.05rem;'>
<span style='font-size: 1rem;'>📊</span>
<span style='font-weight: 600; font-size: 0.9rem; color: var(--text-primary);'>Code Analysis</span>
<span style='font-size: 0.8rem; color: var(--text-secondary);'>File: {System.Net.WebUtility.HtmlEncode(fileName)}</span>
</div>
<div style='display: flex; gap: 1.2rem; margin-top: 0.1rem;'>
<div style='text-align: center;'>
<div style='font-weight: 700; font-size: 1.1rem; color: #ef4444; line-height: 1;'>{errorCount}</div>
<div style='font-size: 0.7rem; color: var(--text-secondary); line-height: 1;'>Errors</div>
</div>
<div style='text-align: center;'>
<div style='font-weight: 700; font-size: 1.1rem; color: #f59e0b; line-height: 1;'>{warningCount}</div>
<div style='font-size: 0.7rem; color: var(--text-secondary); line-height: 1;'>Warnings</div>
</div>
<div style='text-align: center;'>
<div style='font-weight: 700; font-size: 1.1rem; color: #22c55e; line-height: 1;'>{totalIssues}</div>
<div style='font-size: 0.7rem; color: var(--text-secondary); line-height: 1;'>Total</div>
</div>
</div>
</div>");

        // ===== ERRORS SECTION (GROUPED) =====
        if (errorCount > 0)
        {
            html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #ef4444;'></span>
<span style='font-weight: 600; color: #ef4444; font-size: 0.9rem;'>Errors</span>
</div>
<div style='display: flex; flex-direction: column; gap: 0; margin: 0; padding: 0;'>");

            foreach (var error in result.Errors ?? new())
            {
                html.Append($@"<div style='padding: 0.2rem 0.5rem; background: var(--bg-secondary); border-left: 2px solid #ef4444; font-size: 0.8rem; color: #ef4444; line-height: 1.2; margin: 0;'>{System.Net.WebUtility.HtmlEncode(error)}</div>");
            }

            html.Append(@"</div>
</div>");
        }

        // ===== WARNINGS SECTION (GROUPED) =====
        if (warningCount > 0)
        {
            html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #f59e0b;'></span>
<span style='font-weight: 600; color: #f59e0b; font-size: 0.9rem;'>Warnings</span>
</div>
<div style='display: flex; flex-direction: column; gap: 0; margin: 0; padding: 0;'>");

            foreach (var warning in result.Warnings ?? new())
            {
                html.Append($@"<div style='padding: 0.2rem 0.5rem; background: var(--bg-secondary); border-left: 2px solid #f59e0b; font-size: 0.8rem; color: #f59e0b; line-height: 1.2; margin: 0;'>{System.Net.WebUtility.HtmlEncode(warning)}</div>");
            }

            html.Append(@"</div>
</div>");
        }

        // ===== FIXED CODE SECTION - Only show if there are actual changes =====
        if (hasActualFix)
        {
            // Generate diff summary
            var diffSummary = GenerateDiffSummary(result.OriginalCode ?? "", result.FixedCode ?? "");
            
            html.Append($@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #22c55e;'></span>
<span style='font-weight: 600; color: #22c55e; font-size: 0.9rem;'>✅ Fixed Code</span>
<span style='font-size: 0.75rem; color: var(--text-secondary); margin-left: 0.5rem;'>{diffSummary}</span>
</div>
<div style='background: var(--code-bg); color: var(--code-text); padding: 0.3rem 0.5rem; overflow-x: auto; border-left: 2px solid #22c55e; margin: 0;'>
<pre style='margin: 0; font-family: monospace; font-size: 0.75rem; line-height: 1.2; white-space: pre-wrap; word-break: break-word;'>{GenerateDiffHtml(result.OriginalCode ?? "", result.FixedCode ?? "")}</pre>
</div>
</div>");
        }
        else if (!string.IsNullOrEmpty(result.FixedCode))
        {
            // No changes - show info message
            html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #22c55e;'></span>
<span style='font-weight: 600; color: #22c55e; font-size: 0.9rem;'>✅ Fixed Code</span>
</div>
<div style='padding: 0.3rem 0.5rem; font-size: 0.8rem; color: var(--text-secondary); background: var(--bg-secondary); border-left: 2px solid #22c55e; margin: 0;'>
<em>No automatic fixes required - code is syntactically correct.</em>
</div>
</div>");
        }

        // ===== ACTION BUTTONS INFO =====
        html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='padding: 0.3rem 0.5rem; font-size: 0.8rem; color: var(--text-secondary); line-height: 1.2; margin: 0;'>
<strong>💡 Next:</strong> Click <strong>Original</strong>/<strong>Fixed</strong> to compare • <strong>Apply</strong> to download
</div>
</div>");

        // END SINGLE CONTAINER
        html.Append("</div>");

        return html.ToString();
    }
    
    /// <summary>
    /// Generates a summary of differences between original and fixed code
    /// </summary>
    private string GenerateDiffSummary(string original, string fixedCode)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(fixedCode))
            return "";
            
        var originalLines = original.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var fixedLines = fixedCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        int added = 0, removed = 0, modified = 0;
        int maxLen = Math.Max(originalLines.Length, fixedLines.Length);
        
        for (int i = 0; i < maxLen; i++)
        {
            var origLine = i < originalLines.Length ? originalLines[i] : null;
            var fixLine = i < fixedLines.Length ? fixedLines[i] : null;
            
            if (origLine == null && fixLine != null)
                added++;
            else if (origLine != null && fixLine == null)
                removed++;
            else if (origLine != fixLine)
                modified++;
        }
        
        var parts = new List<string>();
        if (added > 0) parts.Add($"+{added} added");
        if (removed > 0) parts.Add($"-{removed} removed");
        if (modified > 0) parts.Add($"~{modified} modified");
        
        return parts.Count > 0 ? $"({string.Join(", ", parts)})" : "";
    }
    
    /// <summary>
    /// Generates HTML diff showing changes between original and fixed code
    /// </summary>
    private string GenerateDiffHtml(string original, string fixedCode)
    {
        if (string.IsNullOrEmpty(fixedCode))
            return "";
            
        var originalLines = (original ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var fixedLines = fixedCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        var html = new StringBuilder();
        int maxLen = Math.Max(originalLines.Length, fixedLines.Length);
        
        // Show up to 30 lines with diff highlighting
        int linesToShow = Math.Min(maxLen, 30);
        
        for (int i = 0; i < linesToShow; i++)
        {
            var origLine = i < originalLines.Length ? originalLines[i] : null;
            var fixLine = i < fixedLines.Length ? fixedLines[i] : null;
            
            string linePrefix = "  ";
            string lineStyle = "";
            string lineContent = fixLine ?? "";
            
            if (origLine == null && fixLine != null)
            {
                // Added line
                linePrefix = "+ ";
                lineStyle = "color: #22c55e; background: rgba(34, 197, 94, 0.1);";
            }
            else if (origLine != null && fixLine == null)
            {
                // Removed line
                linePrefix = "- ";
                lineStyle = "color: #ef4444; background: rgba(239, 68, 68, 0.1); text-decoration: line-through;";
                lineContent = origLine;
            }
            else if (origLine != fixLine)
            {
                // Modified line
                linePrefix = "~ ";
                lineStyle = "color: #f59e0b; background: rgba(245, 158, 11, 0.1);";
            }
            
            var encodedLine = System.Net.WebUtility.HtmlEncode(lineContent);
            if (!string.IsNullOrEmpty(lineStyle))
            {
                html.AppendLine($"<span style='{lineStyle}'>{linePrefix}{encodedLine}</span>");
            }
            else
            {
                html.AppendLine($"{linePrefix}{encodedLine}");
            }
        }
        
        if (maxLen > linesToShow)
        {
            html.AppendLine($"\n... and {maxLen - linesToShow} more lines");
        }
        
        return html.ToString();
    }

    /// <summary>
    /// Rebuilds the analysis response HTML from saved data when loading from history
    /// </summary>
    private string BuildAnalysisResponseFromSavedData(
        string fileName,
        List<string> errors,
        List<string> warnings,
        string? originalCode,
        string? fixedCode)
    {
        var html = new StringBuilder();
        
        var errorCount = errors?.Count ?? 0;
        var warningCount = warnings?.Count ?? 0;
        var totalIssues = errorCount + warningCount;
        
        // Check if there are actual differences between original and fixed
        bool hasActualFix = !string.IsNullOrEmpty(fixedCode) && 
                           !string.IsNullOrEmpty(originalCode) &&
                           !string.Equals(originalCode, fixedCode, StringComparison.Ordinal);

        // START SINGLE CONTAINER
        html.Append(@"<div style='display: flex; flex-direction: column; gap: 0; margin: 0; padding: 0;'>");

        // ===== HEADER WITH STATS =====
        html.Append($@"<div style='padding: 0.3rem 0.5rem; border-bottom: 1px solid var(--border-color); margin: 0;'>
<div style='display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.05rem;'>
<span style='font-size: 1rem;'>📊</span>
<span style='font-weight: 600; font-size: 0.9rem; color: var(--text-primary);'>Code Analysis</span>
<span style='font-size: 0.8rem; color: var(--text-secondary);'>File: {System.Net.WebUtility.HtmlEncode(fileName)}</span>
</div>
<div style='display: flex; gap: 1.2rem; margin-top: 0.1rem;'>
<div style='text-align: center;'>
<div style='font-weight: 700; font-size: 1.1rem; color: #ef4444; line-height: 1;'>{errorCount}</div>
<div style='font-size: 0.7rem; color: var(--text-secondary); line-height: 1;'>Errors</div>
</div>
<div style='text-align: center;'>
<div style='font-weight: 700; font-size: 1.1rem; color: #f59e0b; line-height: 1;'>{warningCount}</div>
<div style='font-size: 0.7rem; color: var(--text-secondary); line-height: 1;'>Warnings</div>
</div>
<div style='text-align: center;'>
<div style='font-weight: 700; font-size: 1.1rem; color: #22c55e; line-height: 1;'>{totalIssues}</div>
<div style='font-size: 0.7rem; color: var(--text-secondary); line-height: 1;'>Total</div>
</div>
</div>
</div>");

        // ===== ERRORS SECTION (GROUPED) =====
        if (errorCount > 0)
        {
            html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #ef4444;'></span>
<span style='font-weight: 600; color: #ef4444; font-size: 0.9rem;'>Errors</span>
</div>
<div style='display: flex; flex-direction: column; gap: 0; margin: 0; padding: 0;'>");

            foreach (var error in errors ?? new List<string>())
            {
                html.Append($@"<div style='padding: 0.2rem 0.5rem; background: var(--bg-secondary); border-left: 2px solid #ef4444; font-size: 0.8rem; color: #ef4444; line-height: 1.2; margin: 0;'>{System.Net.WebUtility.HtmlEncode(error)}</div>");
            }

            html.Append(@"</div>
</div>");
        }

        // ===== WARNINGS SECTION (GROUPED) =====
        if (warningCount > 0)
        {
            html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #f59e0b;'></span>
<span style='font-weight: 600; color: #f59e0b; font-size: 0.9rem;'>Warnings</span>
</div>
<div style='display: flex; flex-direction: column; gap: 0; margin: 0; padding: 0;'>");

            foreach (var warning in warnings ?? new List<string>())
            {
                html.Append($@"<div style='padding: 0.2rem 0.5rem; background: var(--bg-secondary); border-left: 2px solid #f59e0b; font-size: 0.8rem; color: #f59e0b; line-height: 1.2; margin: 0;'>{System.Net.WebUtility.HtmlEncode(warning)}</div>");
            }

            html.Append(@"</div>
</div>");
        }

        // ===== FIXED CODE SECTION - Only show if there are actual changes =====
        if (hasActualFix)
        {
            // Generate diff summary
            var diffSummary = GenerateDiffSummary(originalCode ?? "", fixedCode ?? "");
            
            html.Append($@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #22c55e;'></span>
<span style='font-weight: 600; color: #22c55e; font-size: 0.9rem;'>✅ Fixed Code</span>
<span style='font-size: 0.75rem; color: var(--text-secondary); margin-left: 0.5rem;'>{diffSummary}</span>
</div>
<div style='background: var(--code-bg); color: var(--code-text); padding: 0.3rem 0.5rem; overflow-x: auto; border-left: 2px solid #22c55e; margin: 0;'>
<pre style='margin: 0; font-family: monospace; font-size: 0.75rem; line-height: 1.2; white-space: pre-wrap; word-break: break-word;'>{GenerateDiffHtml(originalCode ?? "", fixedCode ?? "")}</pre>
</div>
</div>");
        }
        else if (!string.IsNullOrEmpty(fixedCode))
        {
            // No changes - show info message
            html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem 0.2rem 0.5rem; margin: 0;'>
<span style='display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #22c55e;'></span>
<span style='font-weight: 600; color: #22c55e; font-size: 0.9rem;'>✅ Fixed Code</span>
</div>
<div style='padding: 0.3rem 0.5rem; font-size: 0.8rem; color: var(--text-secondary); background: var(--bg-secondary); border-left: 2px solid #22c55e; margin: 0;'>
<em>No automatic fixes required - code is syntactically correct.</em>
</div>
</div>");
        }

        // ===== ACTION BUTTONS INFO =====
        html.Append(@"<div style='border-top: 1px solid var(--border-color); margin: 0; padding: 0;'>
<div style='padding: 0.3rem 0.5rem; font-size: 0.8rem; color: var(--text-secondary); line-height: 1.2; margin: 0;'>
<strong>💡 Next:</strong> Click <strong>Original</strong>/<strong>Fixed</strong> to compare • <strong>Apply</strong> to download
</div>
</div>");

        // END SINGLE CONTAINER
        html.Append("</div>");

        return html.ToString();
    }
}
