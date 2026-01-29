using System.Text.RegularExpressions;

namespace LittleHelperAI.Backend.Helpers;

/// <summary>
/// Token counter for accurate credit deduction based on actual token usage.
/// Uses enhanced BPE-like estimation for better accuracy with various LLM backends.
/// </summary>
public static class TokenCounter
{
    private static readonly Regex WordPattern = new(@"\b\w+\b", RegexOptions.Compiled);
    private static readonly Regex SpecialTokenPattern = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex CamelCasePattern = new(@"(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    // Common programming tokens that are typically single tokens
    private static readonly HashSet<string> CommonTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "function", "class", "const", "let", "var", "return", "import", "export",
        "if", "else", "for", "while", "switch", "case", "break", "continue",
        "try", "catch", "throw", "async", "await", "public", "private", "static",
        "interface", "type", "enum", "struct", "namespace", "using", "void",
        "true", "false", "null", "undefined", "this", "new", "delete"
    };

    /// <summary>
    /// Estimate token count for a given text using enhanced BPE-like estimation.
    /// This provides better accuracy for both natural language and code.
    /// </summary>
    public static int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var tokens = 0;

        // Count words with subword estimation
        var words = WordPattern.Matches(text);
        foreach (Match word in words)
        {
            var wordValue = word.Value;

            // Common programming tokens are usually single tokens
            if (CommonTokens.Contains(wordValue))
            {
                tokens += 1;
                continue;
            }

            // Estimate subword tokens based on word length and complexity
            tokens += EstimateWordTokens(wordValue);
        }

        // Count special characters (punctuation, operators)
        var specialChars = SpecialTokenPattern.Matches(text).Count;
        tokens += (specialChars + 1) / 2; // Roughly 0.5 tokens per special char

        // Count numbers separately (often tokenized differently)
        var numbers = NumberPattern.Matches(text);
        foreach (Match num in numbers)
        {
            // Numbers are typically 1 token per 3-4 digits
            tokens += Math.Max(1, (num.Value.Length + 2) / 3);
        }

        // Account for whitespace tokens (newlines, indentation)
        var whitespaceMatches = WhitespacePattern.Matches(text);
        foreach (Match ws in whitespaceMatches)
        {
            if (ws.Value.Contains('\n'))
            {
                tokens += ws.Value.Count(c => c == '\n');
            }
        }

        // Minimum 1 token if text exists
        return Math.Max(1, tokens);
    }

    /// <summary>
    /// Estimate tokens for a single word using BPE-like heuristics.
    /// </summary>
    private static int EstimateWordTokens(string word)
    {
        if (word.Length <= 4)
            return 1;

        // Split camelCase/PascalCase words
        var parts = CamelCasePattern.Split(word);
        if (parts.Length > 1)
        {
            return parts.Sum(p => EstimateWordTokens(p));
        }

        // Longer words are typically split into subwords
        // Average subword length in BPE is around 4-5 characters
        return Math.Max(1, (word.Length + 3) / 4);
    }

    /// <summary>
    /// Count tokens in a conversation (prompt + response).
    /// </summary>
    public static (int promptTokens, int completionTokens, int totalTokens) CountConversationTokens(
        string prompt,
        string response)
    {
        var promptTokens = CountTokens(prompt);
        var completionTokens = CountTokens(response);
        var totalTokens = promptTokens + completionTokens;

        return (promptTokens, completionTokens, totalTokens);
    }

    /// <summary>
    /// Calculate credit cost based on token usage.
    /// </summary>
    /// <param name="totalTokens">Total tokens used</param>
    /// <param name="costPerToken">Cost per token from settings (default: 0.001)</param>
    public static double CalculateCreditCost(int totalTokens, double costPerToken = 0.001)
    {
        return totalTokens * costPerToken;
    }

    /// <summary>
    /// Calculate credit cost from conversation tokens.
    /// </summary>
    /// <param name="prompt">The prompt text</param>
    /// <param name="response">The response text</param>
    /// <param name="costPerToken">Cost per token from settings (default: 0.001)</param>
    public static double CalculateCreditCostFromConversation(
        string prompt,
        string response,
        double costPerToken = 0.001)
    {
        var (_, _, totalTokens) = CountConversationTokens(prompt, response);
        return CalculateCreditCost(totalTokens, costPerToken);
    }

    /// <summary>
    /// Estimate tokens for Ollama models specifically.
    /// Ollama uses different tokenizers depending on the model.
    /// </summary>
    public static int CountTokensForOllama(string text, string? modelName = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Base estimation
        var baseTokens = CountTokens(text);

        // Adjust based on model family (if known)
        var adjustmentFactor = GetOllamaModelAdjustment(modelName);

        return (int)(baseTokens * adjustmentFactor);
    }

    /// <summary>
    /// Get token count adjustment factor for different Ollama model families.
    /// </summary>
    private static double GetOllamaModelAdjustment(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return 1.0;

        var lowerName = modelName.ToLowerInvariant();

        // LLaMA-based models typically have similar tokenization
        if (lowerName.Contains("llama") || lowerName.Contains("codellama"))
            return 1.0;

        // Mistral models may have slightly different tokenization
        if (lowerName.Contains("mistral") || lowerName.Contains("mixtral"))
            return 1.05;

        // Phi models tend to have larger vocabularies
        if (lowerName.Contains("phi"))
            return 0.95;

        // Qwen models
        if (lowerName.Contains("qwen"))
            return 1.1;

        // Default adjustment
        return 1.0;
    }

    /// <summary>
    /// Check if the token count exceeds a limit.
    /// </summary>
    public static bool ExceedsLimit(string text, int maxTokens)
    {
        return CountTokens(text) > maxTokens;
    }

    /// <summary>
    /// Truncate text to fit within a token limit.
    /// Returns the truncated text and the actual token count.
    /// </summary>
    public static (string truncatedText, int tokenCount) TruncateToFit(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, 0);

        var currentTokens = CountTokens(text);
        if (currentTokens <= maxTokens)
            return (text, currentTokens);

        // Binary search for the right truncation point
        var low = 0;
        var high = text.Length;
        var result = string.Empty;
        var resultTokens = 0;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var truncated = text.Substring(0, mid);
            var tokens = CountTokens(truncated);

            if (tokens <= maxTokens)
            {
                low = mid;
                result = truncated;
                resultTokens = tokens;
            }
            else
            {
                high = mid - 1;
            }
        }

        // Try to truncate at a word boundary
        if (result.Length > 0 && result.Length < text.Length)
        {
            var lastSpace = result.LastIndexOf(' ');
            if (lastSpace > result.Length * 0.8) // Only if we don't lose too much
            {
                result = result.Substring(0, lastSpace);
                resultTokens = CountTokens(result);
            }
        }

        return (result + "...", resultTokens + 1);
    }
}
