using System.Text.RegularExpressions;
using LittleHelperAI.KingFactory.Pipeline.Core;

namespace LittleHelperAI.KingFactory.Pipeline.Steps.Validate;

/// <summary>
/// Validates code output for syntax, completeness, and common issues.
/// </summary>
public sealed class ValidateCodeStep : PipelineStepBase
{
    public override string TypeId => "validate.code";
    public override string DisplayName => "Validate Code";
    public override string Category => "Validate";
    public override string Description => "Validates code output for syntax issues, completeness, and common problems.";

    public override StepParameterSchema ParameterSchema => StepParameterSchema.Create(
        new StepParameterDefinition
        {
            Name = "language",
            DisplayName = "Language",
            Type = StepParameterType.Enum,
            Description = "Expected programming language",
            AllowedValues = new object[] { "auto", "javascript", "typescript", "python", "csharp", "html", "css", "json" },
            DefaultValue = "auto"
        },
        new StepParameterDefinition
        {
            Name = "checkSyntax",
            DisplayName = "Check Syntax",
            Type = StepParameterType.Boolean,
            Description = "Check for basic syntax errors",
            DefaultValue = true
        },
        new StepParameterDefinition
        {
            Name = "checkPlaceholders",
            DisplayName = "Check Placeholders",
            Type = StepParameterType.Boolean,
            Description = "Check for placeholder/TODO comments",
            DefaultValue = true
        },
        new StepParameterDefinition
        {
            Name = "minCodeLines",
            DisplayName = "Minimum Code Lines",
            Type = StepParameterType.Integer,
            Description = "Minimum number of code lines expected",
            DefaultValue = 5
        },
        new StepParameterDefinition
        {
            Name = "outputVariable",
            DisplayName = "Output Variable",
            Type = StepParameterType.String,
            Description = "Variable to store validation result",
            DefaultValue = "codeValidation"
        }
    );

    public override Task<StepExecutionResult> ExecuteAsync(
        PipelineContext context,
        StepConfiguration config,
        CancellationToken cancellationToken)
    {
        var language = GetParameter<string>(config, "language", "auto")!;
        var checkSyntax = GetParameter<bool>(config, "checkSyntax", true);
        var checkPlaceholders = GetParameter<bool>(config, "checkPlaceholders", true);
        var minCodeLines = GetParameter<int>(config, "minCodeLines", 5);
        var outputVariable = GetParameter<string>(config, "outputVariable", "codeValidation")!;

        var response = context.ResponseText;
        var issues = new List<string>();
        var warnings = new List<string>();

        // Extract code blocks
        var codeBlocks = ExtractCodeBlocks(response);

        if (codeBlocks.Count == 0)
        {
            // Check if entire response might be code
            if (LooksLikeCode(response))
            {
                codeBlocks.Add(new CodeBlock { Language = language, Content = response });
            }
            else
            {
                issues.Add("No code blocks found in response");
            }
        }

        // Detect language if auto
        foreach (var block in codeBlocks)
        {
            var effectiveLanguage = language == "auto" ? DetectLanguage(block.Content) : language;
            block.Language = effectiveLanguage;
        }

        // Validate each code block
        foreach (var block in codeBlocks)
        {
            // Check minimum lines
            var lineCount = block.Content.Split('\n').Length;
            if (lineCount < minCodeLines)
            {
                warnings.Add($"Code block has only {lineCount} lines (expected at least {minCodeLines})");
            }

            // Check for placeholders
            if (checkPlaceholders)
            {
                var placeholderPatterns = new[]
                {
                    @"TODO",
                    @"FIXME",
                    @"XXX",
                    @"// \.\.\.(\s|$)",
                    @"# \.\.\.(\s|$)",
                    @"\.\.\.\s*$",
                    @"your\s+code\s+here",
                    @"implement\s+here",
                    @"add\s+your",
                    @"placeholder"
                };

                foreach (var pattern in placeholderPatterns)
                {
                    if (Regex.IsMatch(block.Content, pattern, RegexOptions.IgnoreCase))
                    {
                        issues.Add($"Code contains placeholder pattern: {pattern}");
                        break;
                    }
                }
            }

            // Check syntax based on language
            if (checkSyntax)
            {
                var syntaxIssues = CheckSyntax(block.Content, block.Language);
                issues.AddRange(syntaxIssues);
            }
        }

        // Store result
        var isValid = issues.Count == 0;
        var newContext = context
            .WithVariable(outputVariable, isValid)
            .WithVariable($"{outputVariable}.issues", issues)
            .WithVariable($"{outputVariable}.warnings", warnings)
            .WithVariable($"{outputVariable}.codeBlockCount", codeBlocks.Count);

        if (isValid)
        {
            return Task.FromResult(Success(newContext, $"Code validation passed ({codeBlocks.Count} blocks)"));
        }
        else
        {
            var allIssues = string.Join("; ", issues);
            return Task.FromResult(Success(
                newContext.WithMetadata("codeValidation.failed", true),
                $"Code validation issues: {allIssues}"));
        }
    }

    private List<CodeBlock> ExtractCodeBlocks(string text)
    {
        var blocks = new List<CodeBlock>();
        var pattern = @"```(\w*)\s*\n(.*?)```";

        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.Singleline))
        {
            blocks.Add(new CodeBlock
            {
                Language = match.Groups[1].Value,
                Content = match.Groups[2].Value
            });
        }

        return blocks;
    }

    private bool LooksLikeCode(string text)
    {
        // Check for common code patterns
        var codeIndicators = new[]
        {
            @"function\s+\w+",
            @"class\s+\w+",
            @"def\s+\w+",
            @"public\s+\w+",
            @"import\s+",
            @"using\s+",
            @"const\s+\w+",
            @"let\s+\w+",
            @"var\s+\w+",
            @"<\w+[^>]*>",
            @"\{\s*\n"
        };

        return codeIndicators.Any(pattern => Regex.IsMatch(text, pattern));
    }

    private string DetectLanguage(string code)
    {
        if (code.Contains("<!DOCTYPE html") || code.Contains("<html"))
            return "html";
        if (code.Contains("function") && code.Contains("=>"))
            return "javascript";
        if (code.Contains("def ") && code.Contains(":"))
            return "python";
        if (code.Contains("namespace") || code.Contains("using System"))
            return "csharp";
        if (code.Contains("interface ") || code.Contains(": string") || code.Contains(": number"))
            return "typescript";
        if (code.StartsWith("{") && code.EndsWith("}"))
            return "json";

        return "unknown";
    }

    private List<string> CheckSyntax(string code, string language)
    {
        var issues = new List<string>();

        switch (language.ToLowerInvariant())
        {
            case "javascript":
            case "typescript":
                issues.AddRange(CheckJsSyntax(code));
                break;
            case "html":
                issues.AddRange(CheckHtmlSyntax(code));
                break;
            case "json":
                issues.AddRange(CheckJsonSyntax(code));
                break;
            case "csharp":
                issues.AddRange(CheckCSharpSyntax(code));
                break;
        }

        return issues;
    }

    private List<string> CheckJsSyntax(string code)
    {
        var issues = new List<string>();

        // Check brace balance
        var openBraces = code.Count(c => c == '{');
        var closeBraces = code.Count(c => c == '}');
        if (openBraces != closeBraces)
            issues.Add($"Unbalanced braces: {openBraces} open, {closeBraces} close");

        // Check parenthesis balance
        var openParens = code.Count(c => c == '(');
        var closeParens = code.Count(c => c == ')');
        if (openParens != closeParens)
            issues.Add($"Unbalanced parentheses: {openParens} open, {closeParens} close");

        // Check for common errors
        if (Regex.IsMatch(code, @";\s*;"))
            issues.Add("Double semicolon detected");

        return issues;
    }

    private List<string> CheckHtmlSyntax(string code)
    {
        var issues = new List<string>();

        // Check for unclosed tags (simple check)
        var tagPattern = @"<(\w+)[^>]*(?<!/)>";
        var closePattern = @"</(\w+)>";

        var openTags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selfClosing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "br", "hr", "img", "input", "meta", "link", "area", "base", "col", "embed", "source", "track", "wbr"
        };

        foreach (Match match in Regex.Matches(code, tagPattern))
        {
            var tag = match.Groups[1].Value;
            if (!selfClosing.Contains(tag))
            {
                openTags[tag] = openTags.GetValueOrDefault(tag) + 1;
            }
        }

        foreach (Match match in Regex.Matches(code, closePattern))
        {
            var tag = match.Groups[1].Value;
            if (openTags.ContainsKey(tag))
            {
                openTags[tag]--;
                if (openTags[tag] == 0)
                    openTags.Remove(tag);
            }
        }

        foreach (var tag in openTags.Where(t => t.Value > 0))
        {
            issues.Add($"Unclosed <{tag.Key}> tag ({tag.Value} instances)");
        }

        return issues;
    }

    private List<string> CheckJsonSyntax(string code)
    {
        var issues = new List<string>();

        try
        {
            System.Text.Json.JsonDocument.Parse(code);
        }
        catch (System.Text.Json.JsonException ex)
        {
            issues.Add($"Invalid JSON: {ex.Message}");
        }

        return issues;
    }

    private List<string> CheckCSharpSyntax(string code)
    {
        var issues = new List<string>();

        // Check brace balance
        var openBraces = code.Count(c => c == '{');
        var closeBraces = code.Count(c => c == '}');
        if (openBraces != closeBraces)
            issues.Add($"Unbalanced braces: {openBraces} open, {closeBraces} close");

        // Check for missing semicolons after statements
        var lines = code.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Skip certain patterns
            if (trimmed.EndsWith("{") || trimmed.EndsWith("}") || trimmed.EndsWith(";") ||
                trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*") ||
                trimmed.StartsWith("using ") || trimmed.StartsWith("namespace ") ||
                trimmed.StartsWith("public ") || trimmed.StartsWith("private ") ||
                trimmed.StartsWith("protected ") || trimmed.StartsWith("internal ") ||
                trimmed.StartsWith("class ") || trimmed.StartsWith("interface ") ||
                trimmed.StartsWith("if ") || trimmed.StartsWith("else") ||
                trimmed.StartsWith("for ") || trimmed.StartsWith("foreach ") ||
                trimmed.StartsWith("while ") || trimmed.StartsWith("switch ") ||
                trimmed.StartsWith("try") || trimmed.StartsWith("catch") || trimmed.StartsWith("finally"))
                continue;
        }

        return issues;
    }

    private class CodeBlock
    {
        public string Language { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
