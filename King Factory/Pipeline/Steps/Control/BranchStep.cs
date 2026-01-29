using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Control;

/// <summary>
/// Conditionally executes based on a variable value or condition.
/// </summary>
public sealed class BranchStep : PipelineStepBase
{
    public override string TypeId => "control.branch";
    public override string DisplayName => "Branch";
    public override string Category => "Control";
    public override string Description => "Conditionally sets variables or stops execution based on a condition.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "condition",
            DisplayName = "Condition",
            Type = StepParameterType.String,
            Description = "Condition to evaluate (e.g., 'classification == code')",
            Required = true
        },
        new StepParameterDefinition
        {
            Name = "trueVariable",
            DisplayName = "True Variable",
            Type = StepParameterType.String,
            Description = "Variable to set to true if condition is met"
        },
        new StepParameterDefinition
        {
            Name = "falseVariable",
            DisplayName = "False Variable",
            Type = StepParameterType.String,
            Description = "Variable to set to true if condition is not met"
        },
        new StepParameterDefinition
        {
            Name = "stopIfFalse",
            DisplayName = "Stop If False",
            Type = StepParameterType.Boolean,
            Description = "Stop pipeline if condition is false",
            DefaultValue = false
        },
        new StepParameterDefinition
        {
            Name = "message",
            DisplayName = "Stop Message",
            Type = StepParameterType.String,
            Description = "Message to return if stopping"
        }
    );

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var condition = RequireParameter<string>(config, "condition");
        var trueVariable = GetParameter<string>(config, "trueVariable");
        var falseVariable = GetParameter<string>(config, "falseVariable");
        var stopIfFalse = GetParameter<bool>(config, "stopIfFalse", false);
        var message = GetParameter<string>(config, "message");

        var result = EvaluateCondition(context, condition);
        var newContext = context;

        if (result)
        {
            if (!string.IsNullOrEmpty(trueVariable))
            {
                newContext = newContext.WithVariable(trueVariable, true);
            }
            return Task.FromResult(Success(newContext, $"Condition '{condition}' is true"));
        }
        else
        {
            if (!string.IsNullOrEmpty(falseVariable))
            {
                newContext = newContext.WithVariable(falseVariable, true);
            }

            if (stopIfFalse)
            {
                var stopMessage = message ?? $"Condition '{condition}' is false, stopping pipeline";
                return Task.FromResult(new StepExecutionResult
                {
                    Success = true,
                    Context = newContext.WithStop(stopMessage),
                    Output = stopMessage
                });
            }

            return Task.FromResult(Success(newContext, $"Condition '{condition}' is false"));
        }
    }

    private bool EvaluateCondition(PipelineContext context, string condition)
    {
        condition = condition.Trim();

        // Check "exists" condition
        if (condition.EndsWith(" exists", StringComparison.OrdinalIgnoreCase))
        {
            var varName = condition.Substring(0, condition.Length - 7).Trim();
            return context.HasVariable(varName);
        }

        // Check equality
        if (condition.Contains("=="))
        {
            var parts = condition.Split("==", 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var varValue = context.GetVariable<string>(parts[0]);
                var compareValue = parts[1].Trim('"', '\'');
                return string.Equals(varValue, compareValue, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Check inequality
        if (condition.Contains("!="))
        {
            var parts = condition.Split("!=", 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var varValue = context.GetVariable<string>(parts[0]);
                var compareValue = parts[1].Trim('"', '\'');
                return !string.Equals(varValue, compareValue, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Check greater/less than
        if (condition.Contains(">=") || condition.Contains("<=") || condition.Contains(">") || condition.Contains("<"))
        {
            return EvaluateNumericCondition(context, condition);
        }

        // Default: check if variable is truthy
        var val = context.GetVariable<object>(condition);
        return val != null &&
               !val.Equals(false) &&
               !val.Equals(0) &&
               !val.Equals("") &&
               !val.Equals("false");
    }

    private bool EvaluateNumericCondition(PipelineContext context, string condition)
    {
        string op;
        string[] parts;

        if (condition.Contains(">="))
        {
            op = ">=";
            parts = condition.Split(">=", 2, StringSplitOptions.TrimEntries);
        }
        else if (condition.Contains("<="))
        {
            op = "<=";
            parts = condition.Split("<=", 2, StringSplitOptions.TrimEntries);
        }
        else if (condition.Contains(">"))
        {
            op = ">";
            parts = condition.Split(">", 2, StringSplitOptions.TrimEntries);
        }
        else if (condition.Contains("<"))
        {
            op = "<";
            parts = condition.Split("<", 2, StringSplitOptions.TrimEntries);
        }
        else
        {
            return false;
        }

        if (parts.Length != 2)
            return false;

        var varValue = context.GetVariable<double?>(parts[0]) ?? 0;
        if (!double.TryParse(parts[1], out var compareValue))
            return false;

        return op switch
        {
            ">=" => varValue >= compareValue,
            "<=" => varValue <= compareValue,
            ">" => varValue > compareValue,
            "<" => varValue < compareValue,
            _ => false
        };
    }
}
