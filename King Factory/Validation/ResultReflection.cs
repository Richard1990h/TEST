using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Prompts;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Validation;

/// <summary>
/// Reflects on results and suggests improvements.
/// </summary>
public interface IResultReflection
{
    /// <summary>
    /// Reflect on a result and suggest improvements.
    /// </summary>
    Task<ReflectionResult> ReflectAsync(string output, string originalQuery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine if a result needs correction.
    /// </summary>
    Task<bool> NeedsCorrectionAsync(string output, string originalQuery, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of reflection.
/// </summary>
public class ReflectionResult
{
    /// <summary>
    /// Whether the output is satisfactory.
    /// </summary>
    public bool IsSatisfactory { get; set; }

    /// <summary>
    /// Suggested improvements.
    /// </summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>
    /// Corrected output if needed.
    /// </summary>
    public string? CorrectedOutput { get; set; }

    /// <summary>
    /// Confidence in the assessment.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Aspects that were evaluated.
    /// </summary>
    public Dictionary<string, bool> EvaluatedAspects { get; set; } = new();
}

/// <summary>
/// LLM-based result reflection.
/// </summary>
public class ResultReflection : IResultReflection
{
    private readonly ILogger<ResultReflection> _logger;
    private readonly ILlmEngine _llmEngine;
    private readonly ISystemPrompts _systemPrompts;
    private readonly IValidationPass _validationPass;

    public ResultReflection(
        ILogger<ResultReflection> logger,
        ILlmEngine llmEngine,
        ISystemPrompts systemPrompts,
        IValidationPass validationPass)
    {
        _logger = logger;
        _llmEngine = llmEngine;
        _systemPrompts = systemPrompts;
        _validationPass = validationPass;
    }

    public async Task<ReflectionResult> ReflectAsync(string output, string originalQuery, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reflecting on output for query: {Query}",
            originalQuery.Substring(0, Math.Min(50, originalQuery.Length)));

        var result = new ReflectionResult();

        // First, run static validation
        var validationResult = _validationPass.Validate(output, new ValidationContext
        {
            OriginalQuery = originalQuery
        });

        result.EvaluatedAspects["StaticValidation"] = validationResult.IsValid;

        if (!validationResult.IsValid)
        {
            result.IsSatisfactory = false;
            result.Suggestions.AddRange(validationResult.Issues.Select(i => i.Message));
            result.CorrectedOutput = validationResult.SanitizedOutput;
            result.Confidence = validationResult.Confidence;
            return result;
        }

        // For simple queries, skip LLM reflection
        if (IsSimpleQuery(originalQuery))
        {
            result.IsSatisfactory = true;
            result.Confidence = 0.9;
            return result;
        }

        // Use LLM for deeper reflection
        var reflectionPrompt = BuildReflectionPrompt(output, originalQuery);
        var reflectionResponse = await GenerateResponseAsync(reflectionPrompt, cancellationToken);

        // Parse reflection response
        ParseReflectionResponse(reflectionResponse, result);

        _logger.LogDebug("Reflection complete: Satisfactory={IsSatisfactory}, Confidence={Confidence}",
            result.IsSatisfactory, result.Confidence);

        return result;
    }

    public async Task<bool> NeedsCorrectionAsync(string output, string originalQuery, CancellationToken cancellationToken = default)
    {
        // Quick checks first
        var validationResult = _validationPass.Validate(output, new ValidationContext
        {
            OriginalQuery = originalQuery
        });

        if (!validationResult.IsValid)
        {
            return true;
        }

        // Check for obvious issues
        if (string.IsNullOrWhiteSpace(output))
            return true;

        if (output.Length < 10 && originalQuery.Length > 50)
            return true;

        // For complex queries, use LLM
        if (!IsSimpleQuery(originalQuery))
        {
            var reflection = await ReflectAsync(output, originalQuery, cancellationToken);
            return !reflection.IsSatisfactory;
        }

        return false;
    }

    private bool IsSimpleQuery(string query)
    {
        // Simple queries are short and don't require complex analysis
        if (query.Length < 50)
            return true;

        // Greetings and simple commands
        var simplePatterns = new[]
        {
            "hello", "hi", "hey", "thanks", "thank you",
            "yes", "no", "ok", "okay"
        };

        var lowerQuery = query.ToLowerInvariant();
        return simplePatterns.Any(p => lowerQuery.StartsWith(p));
    }

    private string BuildReflectionPrompt(string output, string query)
    {
        return $@"{_systemPrompts.GetValidationPrompt()}

## Original Query
{query}

## Generated Output
{output}

## Instructions
Evaluate this output and respond with:
1. SATISFACTORY: yes/no
2. CONFIDENCE: 0.0-1.0
3. ISSUES: List any problems (one per line, starting with -)
4. SUGGESTIONS: List improvements (one per line, starting with -)

Be concise. Focus on:
- Does it answer the query?
- Is it accurate?
- Is it complete?
- Is it safe?";
    }

    private async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = new System.Text.StringBuilder();

        await foreach (var token in _llmEngine.StreamAsync(prompt, maxTokens: 300, cancellationToken: cancellationToken))
        {
            response.Append(token);
        }

        return response.ToString();
    }

    private void ParseReflectionResponse(string response, ReflectionResult result)
    {
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("SATISFACTORY:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed.Substring("SATISFACTORY:".Length).Trim().ToLowerInvariant();
                result.IsSatisfactory = value == "yes" || value == "true";
            }
            else if (trimmed.StartsWith("CONFIDENCE:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed.Substring("CONFIDENCE:".Length).Trim();
                if (double.TryParse(value, out var confidence))
                {
                    result.Confidence = Math.Max(0, Math.Min(1, confidence));
                }
            }
            else if (trimmed.StartsWith("-"))
            {
                result.Suggestions.Add(trimmed.Substring(1).Trim());
            }
        }

        // Default values if parsing failed
        if (result.Confidence == 0 && result.Suggestions.Count == 0)
        {
            result.IsSatisfactory = true;
            result.Confidence = 0.7;
        }
    }
}
