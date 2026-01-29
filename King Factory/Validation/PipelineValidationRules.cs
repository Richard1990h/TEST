namespace LittleHelperAI.KingFactory.Validation;

/// <summary>
/// Configuration for pipeline-specific validation rules.
/// </summary>
public class PipelineValidationRules
{
    /// <summary>
    /// Pipeline ID these rules apply to.
    /// </summary>
    public string PipelineId { get; set; } = string.Empty;

    /// <summary>
    /// Whether validation is enabled for this pipeline.
    /// </summary>
    public bool ValidationEnabled { get; set; } = true;

    /// <summary>
    /// Whether to validate output type matches expected.
    /// </summary>
    public bool ValidateOutputType { get; set; } = true;

    /// <summary>
    /// Whether to check for sensitive data.
    /// </summary>
    public bool CheckSensitiveData { get; set; } = true;

    /// <summary>
    /// Whether to check for hallucination indicators.
    /// </summary>
    public bool CheckHallucination { get; set; } = true;

    /// <summary>
    /// Whether to validate response relevance.
    /// </summary>
    public bool CheckRelevance { get; set; } = true;

    /// <summary>
    /// Minimum confidence threshold.
    /// </summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Maximum output length in characters.
    /// </summary>
    public int? MaxOutputLength { get; set; }

    /// <summary>
    /// Expected output type for this pipeline.
    /// </summary>
    public OutputType? ExpectedOutputType { get; set; }

    /// <summary>
    /// Custom validation rules to apply.
    /// </summary>
    public List<string> CustomRules { get; set; } = new();

    /// <summary>
    /// Words/phrases that should trigger warnings.
    /// </summary>
    public List<string> WarningPhrases { get; set; } = new();

    /// <summary>
    /// Words/phrases that should trigger errors.
    /// </summary>
    public List<string> ErrorPhrases { get; set; } = new();
}

/// <summary>
/// Registry of validation rules per pipeline.
/// </summary>
public interface IPipelineValidationRules
{
    /// <summary>
    /// Get validation rules for a pipeline.
    /// </summary>
    PipelineValidationRules GetRules(string pipelineId);

    /// <summary>
    /// Register custom rules for a pipeline.
    /// </summary>
    void RegisterRules(PipelineValidationRules rules);
}

/// <summary>
/// Implementation of pipeline validation rules registry.
/// </summary>
public class PipelineValidationRulesRegistry : IPipelineValidationRules
{
    private readonly Dictionary<string, PipelineValidationRules> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly PipelineValidationRules _defaultRules;

    public PipelineValidationRulesRegistry()
    {
        _defaultRules = new PipelineValidationRules
        {
            PipelineId = "default",
            ValidationEnabled = true,
            ValidateOutputType = true,
            CheckSensitiveData = true,
            CheckHallucination = true,
            CheckRelevance = true,
            MinConfidence = 0.5
        };

        // Register default rules for known pipelines
        RegisterDefaults();
    }

    public PipelineValidationRules GetRules(string pipelineId)
    {
        if (string.IsNullOrEmpty(pipelineId))
            return _defaultRules;

        return _rules.TryGetValue(pipelineId, out var rules)
            ? rules
            : _defaultRules;
    }

    public void RegisterRules(PipelineValidationRules rules)
    {
        _rules[rules.PipelineId] = rules;
    }

    private void RegisterDefaults()
    {
        // Code generation pipeline - strict validation
        RegisterRules(new PipelineValidationRules
        {
            PipelineId = "code",
            ValidationEnabled = true,
            ValidateOutputType = true,
            ExpectedOutputType = OutputType.Code,
            CheckSensitiveData = true,
            CheckHallucination = true,
            CheckRelevance = true,
            MinConfidence = 0.7,
            ErrorPhrases = new List<string>
            {
                "I cannot write code",
                "I'm unable to generate",
                "I cannot access files"
            }
        });

        // Bolt parody pipeline - tool-focused validation
        RegisterRules(new PipelineValidationRules
        {
            PipelineId = "bolt-parody",
            ValidationEnabled = true,
            ValidateOutputType = false, // Flexible output
            CheckSensitiveData = true,
            CheckHallucination = false, // Less strict
            CheckRelevance = false,
            MinConfidence = 0.3
        });

        // Chat pipeline - standard validation
        RegisterRules(new PipelineValidationRules
        {
            PipelineId = "chat",
            ValidationEnabled = true,
            ValidateOutputType = false,
            CheckSensitiveData = true,
            CheckHallucination = true,
            CheckRelevance = true,
            MinConfidence = 0.5
        });

        // Tool-heavy pipeline - minimal validation
        RegisterRules(new PipelineValidationRules
        {
            PipelineId = "tools",
            ValidationEnabled = true,
            ValidateOutputType = false,
            CheckSensitiveData = true,
            CheckHallucination = false,
            CheckRelevance = false,
            MinConfidence = 0.3
        });

        // Planning pipeline - moderate validation
        RegisterRules(new PipelineValidationRules
        {
            PipelineId = "planning",
            ValidationEnabled = true,
            ValidateOutputType = false,
            CheckSensitiveData = true,
            CheckHallucination = true,
            CheckRelevance = true,
            MinConfidence = 0.6,
            WarningPhrases = new List<string>
            {
                "I'm not sure",
                "might not work",
                "could be incorrect"
            }
        });
    }
}

/// <summary>
/// Extended validation pass that uses pipeline-specific rules.
/// </summary>
public static class ValidationPassExtensions
{
    /// <summary>
    /// Validate output using pipeline-specific rules.
    /// </summary>
    public static ValidationPassResult ValidateWithRules(
        this IValidationPass validationPass,
        string output,
        ValidationContext context,
        PipelineValidationRules rules)
    {
        // If validation is disabled, return valid
        if (!rules.ValidationEnabled)
        {
            return new ValidationPassResult
            {
                IsValid = true,
                SanitizedOutput = output,
                Confidence = 1.0
            };
        }

        // Apply expected output type from rules
        if (rules.ExpectedOutputType.HasValue)
        {
            context.ExpectedType = rules.ExpectedOutputType.Value;
        }

        // Run base validation
        var result = validationPass.Validate(output, context);

        // Check max output length
        if (rules.MaxOutputLength.HasValue && output.Length > rules.MaxOutputLength.Value)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Code = "OUTPUT_TOO_LONG",
                Message = $"Output exceeds maximum length of {rules.MaxOutputLength} characters"
            });
        }

        // Check for warning phrases
        foreach (var phrase in rules.WarningPhrases)
        {
            if (output.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Code = "WARNING_PHRASE",
                    Message = $"Output contains warning phrase: '{phrase}'"
                });
                result.Confidence -= 0.1;
            }
        }

        // Check for error phrases
        foreach (var phrase in rules.ErrorPhrases)
        {
            if (output.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "ERROR_PHRASE",
                    Message = $"Output contains error phrase: '{phrase}'"
                });
                result.IsValid = false;
            }
        }

        // Check minimum confidence
        if (result.Confidence < rules.MinConfidence)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Code = "LOW_CONFIDENCE",
                Message = $"Confidence {result.Confidence:P0} is below threshold {rules.MinConfidence:P0}"
            });
        }

        // Ensure confidence stays in bounds
        result.Confidence = Math.Max(0, Math.Min(1, result.Confidence));

        return result;
    }

    /// <summary>
    /// Validate a general response (non-code).
    /// </summary>
    public static ValidationPassResult ValidateResponse(
        this IValidationPass validationPass,
        string output,
        string? originalQuery = null)
    {
        var context = new ValidationContext
        {
            OriginalQuery = originalQuery,
            ExpectedType = OutputType.Text,
            ExpectsCode = false,
            ExpectsFilePaths = false
        };

        return validationPass.Validate(output, context);
    }
}
