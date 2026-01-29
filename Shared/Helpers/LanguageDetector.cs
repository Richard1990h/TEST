namespace LittleHelperAI.Shared.Helpers;

/// <summary>
/// Detects programming language from file name and content
/// </summary>
public static class LanguageDetector
{
    /// <summary>
    /// Detect the programming language from file name and optional content
    /// </summary>
    /// <param name="fileName">The file name</param>
    /// <param name="content">Optional file content for additional detection</param>
    /// <returns>The detected language name</returns>
    public static string Detect(string fileName, string? content = null)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();

        var language = ext switch
        {
            ".cs" => "csharp",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".js" => "javascript",
            ".jsx" => "javascript",
            ".py" => "python",
            ".java" => "java",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".h" or ".hpp" => "cpp",
            ".rs" => "rust",
            ".go" => "go",
            ".rb" => "ruby",
            ".kt" or ".kts" => "kotlin",
            ".swift" => "swift",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".scss" or ".sass" => "scss",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".md" or ".markdown" => "markdown",
            ".sql" => "sql",
            ".sh" or ".bash" => "bash",
            ".ps1" => "powershell",
            ".razor" => "razor",
            ".cshtml" => "razor",
            ".php" => "php",
            _ => DetectFromContent(content)
        };

        return language;
    }

    private static string DetectFromContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "plaintext";

        // Check for common patterns in content
        if (content.Contains("namespace") && content.Contains("class") && content.Contains("{"))
            return "csharp";

        if (content.Contains("def ") && content.Contains(":") && !content.Contains("{"))
            return "python";

        if (content.Contains("function") || content.Contains("const ") || content.Contains("let "))
            return "javascript";

        if (content.Contains("import ") && content.Contains("interface") && content.Contains(":"))
            return "typescript";

        if (content.Contains("package main") || content.Contains("func main"))
            return "go";

        if (content.Contains("fn main") && content.Contains("->"))
            return "rust";

        return "plaintext";
    }
}
