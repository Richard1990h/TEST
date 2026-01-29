using System.Text.RegularExpressions;

namespace LittleHelperAI.Backend.Helpers;

public enum QueryIntent
{
    General,
    Math,
    CodeFix,
    CodeExplain,
    NeedsWeb,
    FallbackLLM
}

public static class QueryRouter
{
    // Extremely cheap routing (string + regex only)
    public static QueryIntent DetectIntent(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return QueryIntent.General;

        var input = rawInput.ToLowerInvariant();

        // -------------------------
        // CODE FIX (human-typo tolerant)
        // -------------------------
        if (Regex.IsMatch(input,
            @"\b(fix|fex|corr+ect|correct|er+or|error|except+ion|exception|bug|issue|compile|build|stacktrace|crash|broken)\b"))
            return QueryIntent.CodeFix;

        // -------------------------
        // CODE EXPLAIN
        // -------------------------
        if (Regex.IsMatch(input,
            @"\b(explain\s+code|explain\s+this|what\s+does\s+this\s+do|how\s+does\s+this\s+work)\b"))
            return QueryIntent.CodeExplain;

        // -------------------------
        // MATH (disabled - MathSolver removed)
        // -------------------------

        // -------------------------
        // WEB REQUIRED
        // -------------------------
        if (Regex.IsMatch(input,
            @"\b(latest|news|today|current|price\s+of|weather|who\s+is|what\s+is\s+the\s+latest|score|stock)\b"))
            return QueryIntent.NeedsWeb;

        return QueryIntent.General;
    }

    // ✅ REQUIRED by ChatController & legacy code
    // DO NOT REMOVE – returns enum (not string)
    public static QueryIntent AnalyzeIntent(string rawInput)
    {
        return DetectIntent(rawInput);
    }
}
