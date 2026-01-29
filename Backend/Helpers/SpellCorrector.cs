using System.Text;
using LittleHelperAI.Backend.Utils;

namespace LittleHelperAI.Backend.Helpers;

/// <summary>
/// Human-like spell corrector with fuzzy intent tolerance.
/// Optimized for speed, slang, missing letters, and phonetic mistakes.
/// Safe for code (will not mutate code-like input).
/// </summary>
public static class SpellCorrector
{
    /* =========================
       CONFIG (TUNE HERE)
    ========================== */

    // Human tolerance level (0.0–1.0). Higher = more forgiving.
    private const double SimilarityThreshold = 0.72;

    // Ignore very short words
    private const int MinTokenLength = 4;
    private const int MaxTokenLength = 24;

    /* =========================
       DIRECT MISSPELLINGS
    ========================== */

    private static readonly Dictionary<string, string> DirectMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common speech / human typing
        ["wat"] = "what",
        ["wats"] = "what is",
        ["r"] = "are",
        ["u"] = "you",
        ["ur"] = "your",
        ["pls"] = "please",
        ["plz"] = "please",
        ["thx"] = "thanks",
        ["cuz"] = "because",
        ["bc"] = "because",

        // Existing mappings (kept)
        ["mathamatic"] = "mathematics",
        ["mathamatics"] = "mathematics",
        ["marth"] = "math",
        ["algerbra"] = "algebra",
        ["trignometry"] = "trigonometry",
        ["calculas"] = "calculus",
        ["geomtry"] = "geometry",
        ["algoritm"] = "algorithm",
        ["algorythm"] = "algorithm",
        ["corrrect"] = "correct",
        ["eror"] = "error",
        ["exeption"] = "exception",
        ["functon"] = "function",
        ["methd"] = "method",
        ["varialbe"] = "variable",
        ["aray"] = "array",
        ["objet"] = "object",
        ["classe"] = "class",
        ["javscript"] = "javascript",
        ["javasript"] = "javascript",
        ["pythin"] = "python",
        ["pyton"] = "python",
        ["cshrp"] = "csharp",
        ["databse"] = "database",
        ["databas"] = "database",
        ["mongdb"] = "mongodb",
        ["mysqll"] = "mysql",
        ["reddis"] = "redis",
        ["teh"] = "the",
        ["adn"] = "and",
        ["taht"] = "that",
        ["thsi"] = "this",
        ["waht"] = "what",
        ["whcih"] = "which"
    };

    /* =========================
       TECH VOCAB (UNCHANGED)
    ========================== */

    private static readonly HashSet<string> TechnicalVocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        // (UNCHANGED — your full list stays here)
        "math","mathematics","algebra","geometry","trigonometry","calculus",
        "programming","code","algorithm","variable","function","class","object",
        "python","javascript","typescript","java","csharp","cpp",
        "react","angular","vue","django","flask","express",
        "database","sql","mysql","postgresql","mongodb","redis",
        "api","sdk","http","https","rest","graphql",
        "error","exception","debug","stacktrace"
    };

    /* =========================
       CODE PROTECTION
    ========================== */

    private static readonly HashSet<string> CodePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "var","let","const","def","func","fn","int","bool","true","false",
        "null","undefined","void","if","else","for","while","return",
        "class","public","private","protected","static","async","await"
    };

    /* =========================
       PUBLIC ENTRY
    ========================== */

    public static string NormalizeKeywords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // Never touch code
        if (LooksLikeCode(input))
            return input;

        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length; i++)
        {
            var original = tokens[i];
            var clean = TrimPunctuation(original);

            if (clean.Length < MinTokenLength || clean.Length > MaxTokenLength)
                continue;

            if (CodePatterns.Contains(clean))
                continue;

            if (TechnicalVocabulary.Contains(clean))
                continue;

            // Direct map first (fastest)
            if (DirectMap.TryGetValue(clean, out var mapped))
            {
                tokens[i] = original.Replace(clean, mapped, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            // Human fuzzy match
            var best = FindBestHumanMatch(clean);
            if (best != null)
            {
                tokens[i] = original.Replace(clean, best, StringComparison.OrdinalIgnoreCase);
            }
        }

        return string.Join(' ', tokens);
    }

    /* =========================
       HUMAN FUZZY MATCH
    ========================== */

    private static string? FindBestHumanMatch(string token)
    {
        string? best = null;
        double bestScore = 0;

        foreach (var term in TechnicalVocabulary)
        {
            // Quick length filter (huge speed win)
            if (Math.Abs(term.Length - token.Length) > term.Length)
                continue;

            double score = Similarity(token, term);

            if (score > bestScore)
            {
                bestScore = score;
                best = term;
            }
        }

        return bestScore >= SimilarityThreshold ? best : null;
    }

    /// <summary>
    /// Normalized similarity (0–1) using Levenshtein
    /// Human-like tolerance instead of strict distance
    /// </summary>
    private static double Similarity(string a, string b)
    {
        int dist = FuzzyMatch.LevenshteinDistance(a.ToLower(), b.ToLower());
        int maxLen = Math.Max(a.Length, b.Length);

        if (maxLen == 0) return 1.0;
        return 1.0 - (double)dist / maxLen;
    }

    /* =========================
       UTIL
    ========================== */

    private static string TrimPunctuation(string s)
    {
        return s.Trim('.', ',', ';', ':', '!', '?', ')', '(', '"', '\'', '[', ']', '{', '}');
    }

    private static bool LooksLikeCode(string text)
    {
        var indicators = new[]
        {
            "```","function","class","def ","var ","let ","const ",
            "=>","==","!=","&&","||","public ","private ","static "
        };

        foreach (var i in indicators)
            if (text.Contains(i, StringComparison.OrdinalIgnoreCase))
                return true;

        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[a-z]+[A-Z]\w*\b")) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\w+_\w+\b")) return true;

        return false;
    }
}
