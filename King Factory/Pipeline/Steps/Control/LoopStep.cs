using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Control;

/// <summary>
/// Loops over a collection or repeats a fixed number of times.
/// Note: This step sets up loop context but doesn't execute sub-steps directly.
/// Use in combination with condition-based steps.
/// </summary>
public sealed class LoopStep : PipelineStepBase
{
    public override string TypeId => "control.loop";
    public override string DisplayName => "Loop";
    public override string Category => "Control";
    public override string Description => "Sets up a loop counter or iterates over a collection, storing the current item in a variable.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "count",
            DisplayName = "Count",
            Type = StepParameterType.Integer,
            Description = "Number of times to loop (if not using collection)",
            DefaultValue = 1,
            MinValue = 0,
            MaxValue = 100
        },
        new StepParameterDefinition
        {
            Name = "collectionVariable",
            DisplayName = "Collection Variable",
            Type = StepParameterType.String,
            Description = "Variable containing array/collection to iterate over"
        },
        new StepParameterDefinition
        {
            Name = "itemVariable",
            DisplayName = "Item Variable",
            Type = StepParameterType.String,
            Description = "Variable to store current item",
            DefaultValue = "loopItem"
        },
        new StepParameterDefinition
        {
            Name = "indexVariable",
            DisplayName = "Index Variable",
            Type = StepParameterType.String,
            Description = "Variable to store current index",
            DefaultValue = "loopIndex"
        },
        new StepParameterDefinition
        {
            Name = "countVariable",
            DisplayName = "Count Variable",
            Type = StepParameterType.String,
            Description = "Variable to store total count",
            DefaultValue = "loopCount"
        }
    );

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var count = GetParameter<int>(config, "count", 1);
        var collectionVariable = GetParameter<string>(config, "collectionVariable");
        var itemVariable = GetParameter<string>(config, "itemVariable", "loopItem")!;
        var indexVariable = GetParameter<string>(config, "indexVariable", "loopIndex")!;
        var countVariable = GetParameter<string>(config, "countVariable", "loopCount")!;

        var newContext = context;

        if (!string.IsNullOrEmpty(collectionVariable))
        {
            // Iterate over collection
            var collection = context.GetVariable<object[]>(collectionVariable);
            if (collection == null || collection.Length == 0)
            {
                newContext = newContext
                    .WithVariable(countVariable, 0)
                    .WithVariable(indexVariable, -1);
                return Task.FromResult(Success(newContext, "Empty collection"));
            }

            // Get current index (or start at 0)
            var currentIndex = context.GetVariable<int?>(indexVariable) ?? -1;
            currentIndex++;

            if (currentIndex >= collection.Length)
            {
                // Loop complete
                newContext = newContext
                    .WithVariable($"{indexVariable}.complete", true);
                return Task.FromResult(Success(newContext, "Loop complete"));
            }

            // Set current item
            newContext = newContext
                .WithVariable(itemVariable, collection[currentIndex])
                .WithVariable(indexVariable, currentIndex)
                .WithVariable(countVariable, collection.Length);

            return Task.FromResult(Success(newContext, $"Loop iteration {currentIndex + 1}/{collection.Length}"));
        }
        else
        {
            // Fixed count loop
            var currentIndex = context.GetVariable<int?>(indexVariable) ?? -1;
            currentIndex++;

            if (currentIndex >= count)
            {
                newContext = newContext
                    .WithVariable($"{indexVariable}.complete", true);
                return Task.FromResult(Success(newContext, "Loop complete"));
            }

            newContext = newContext
                .WithVariable(indexVariable, currentIndex)
                .WithVariable(countVariable, count);

            return Task.FromResult(Success(newContext, $"Loop iteration {currentIndex + 1}/{count}"));
        }
    }
}
