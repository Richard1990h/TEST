using System.Text.Json;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.LLM;

/// <summary>
/// Classifies input into predefined categories using LLM.
/// </summary>
public sealed class LlmClassifyStep : PipelineStepBase
{
    private readonly IUnifiedLlmProvider _llmProvider;

    public override string TypeId => "llm.classify";
    public override string DisplayName => "LLM Classify";
    public override string Category => "LLM";
    public override string Description => "Classifies the input into one of the predefined categories and stores the result in a variable.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "categories",
            DisplayName = "Categories",
            Type = StepParameterType.StringArray,
            Description = "List of categories to classify into",
            Required = true
        },
        new StepParameterDefinition
        {
            Name = "outputVariable",
            DisplayName = "Output Variable",
            Type = StepParameterType.String,
            Description = "Variable name to store the classification result",
            DefaultValue = "classification"
        },
        new StepParameterDefinition
        {
            Name = "includeConfidence",
            DisplayName = "Include Confidence",
            Type = StepParameterType.Boolean,
            Description = "Also output confidence score",
            DefaultValue = true
        },
        new StepParameterDefinition
        {
            Name = "temperature",
            DisplayName = "Temperature",
            Type = StepParameterType.Float,
            Description = "Lower temperature for more deterministic classification",
            DefaultValue = 0.1f
        }
    );

    public LlmClassifyStep(IUnifiedLlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public override async Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var categories = GetParameter<string[]>(config, "categories");
        if (categories == null || categories.Length == 0)
        {
            return Failure(context, "No categories specified for classification");
        }

        var outputVariable = GetParameter<string>(config, "outputVariable", "classification")!;
        var includeConfidence = GetParameter<bool>(config, "includeConfidence", true);
        var temperature = GetParameter<float>(config, "temperature", 0.1f);

        // Build classification prompt
        var categoriesList = string.Join(", ", categories.Select(c => $"\"{c}\""));
        var prompt = $@"<|im_start|>system
You are a classifier. Analyze the following text and classify it into exactly ONE of these categories: [{categoriesList}]

Respond ONLY with a JSON object in this format:
{{
  ""category"": ""<selected category>"",
  ""confidence"": <0.0-1.0>
}}
<|im_end|>
<|im_start|>user
{context.Input.Message}
<|im_end|>
<|im_start|>assistant
";

        var response = await _llmProvider.GenerateAsync(prompt, 100, temperature, cancellationToken);

        // Parse classification result
        try
        {
            var json = ExtractJson(response);
            if (!string.IsNullOrEmpty(json))
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var category = root.GetProperty("category").GetString() ?? categories[0];
                var confidence = 0.5f;

                if (root.TryGetProperty("confidence", out var confProp))
                {
                    confidence = (float)confProp.GetDouble();
                }

                // Validate category is in list
                if (!categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                {
                    // Find closest match
                    category = categories.FirstOrDefault(c =>
                        category.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                        c.Contains(category, StringComparison.OrdinalIgnoreCase)) ?? categories[0];
                }

                var newContext = context
                    .WithVariable(outputVariable, category)
                    .WithVariable($"{outputVariable}.confidence", confidence);

                return Success(newContext, $"Classified as '{category}' with confidence {confidence:P0}");
            }
        }
        catch (JsonException)
        {
            // Fall through to default handling
        }

        // Default to first category if parsing fails
        var defaultContext = context
            .WithVariable(outputVariable, categories[0])
            .WithVariable($"{outputVariable}.confidence", 0.5f);

        return Success(defaultContext, $"Defaulted to '{categories[0]}' (parsing failed)");
    }

    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1);
        }
        return null;
    }
}
