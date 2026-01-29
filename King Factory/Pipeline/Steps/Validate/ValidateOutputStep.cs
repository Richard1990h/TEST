using LittleHelperAI.KingFactory.Pipeline.Core;
using LittleHelperAI.KingFactory.Validation;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Validate;

/// <summary>
/// Validates the LLM output for quality, completeness, and safety.
/// </summary>
public sealed class ValidateOutputStep : PipelineStepBase
{
    private readonly IValidationPass _validationPass;

    public override string TypeId => "validate.output";
    public override string DisplayName => "Validate Output";
    public override string Category => "Validate";
    public override string Description => "Validates the LLM output for quality, completeness, and safety issues.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "strictMode",
            DisplayName = "Strict Mode",
            Type = StepParameterType.Boolean,
            Description = "Enable strict validation (fail on warnings)",
            DefaultValue = false
        },
        new StepParameterDefinition
        {
            Name = "checkEmpty",
            DisplayName = "Check Empty",
            Type = StepParameterType.Boolean,
            Description = "Fail if response is empty",
            DefaultValue = true
        },
        new StepParameterDefinition
        {
            Name = "minLength",
            DisplayName = "Minimum Length",
            Type = StepParameterType.Integer,
            Description = "Minimum response length (0 = no minimum)",
            DefaultValue = 0
        },
        new StepParameterDefinition
        {
            Name = "outputVariable",
            DisplayName = "Output Variable",
            Type = StepParameterType.String,
            Description = "Variable to store validation result",
            DefaultValue = "validationResult"
        }
    );

    public ValidateOutputStep(IValidationPass validationPass)
    {
        _validationPass = validationPass;
    }

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var strictMode = GetParameter<bool>(config, "strictMode", false);
        var checkEmpty = GetParameter<bool>(config, "checkEmpty", true);
        var minLength = GetParameter<int>(config, "minLength", 0);
        var outputVariable = GetParameter<string>(config, "outputVariable", "validationResult")!;

        var response = context.ResponseText;
        var issues = new List<string>();
        var warnings = new List<string>();

        // Check empty
        if (checkEmpty && string.IsNullOrWhiteSpace(response))
        {
            issues.Add("Response is empty");
        }

        // Check minimum length
        if (minLength > 0 && response.Length < minLength)
        {
            issues.Add($"Response is too short (minimum: {minLength} characters)");
        }

        // Run validation pass
        var validationContext = new ValidationContext
        {
            OriginalQuery = context.Input.Message,
            ExpectedType = OutputType.Text
        };

        var validationResult = _validationPass.Validate(response, validationContext);

        foreach (var issue in validationResult.Issues)
        {
            if (issue.Severity == ValidationSeverity.Error)
            {
                issues.Add(issue.Message);
            }
            else
            {
                warnings.Add(issue.Message);
            }
        }

        // Store result
        var isValid = issues.Count == 0 && (!strictMode || warnings.Count == 0);
        var newContext = context
            .WithVariable(outputVariable, isValid)
            .WithVariable($"{outputVariable}.issues", issues)
            .WithVariable($"{outputVariable}.warnings", warnings);

        if (isValid)
        {
            return Task.FromResult(Success(newContext, "Validation passed"));
        }
        else
        {
            var allIssues = string.Join("; ", issues);
            return Task.FromResult(Success(
                newContext.WithMetadata("validation.failed", true).WithMetadata("validation.issues", allIssues),
                $"Validation issues: {allIssues}"));
        }
    }
}
