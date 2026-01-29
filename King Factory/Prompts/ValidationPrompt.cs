namespace LittleHelperAI.KingFactory.Prompts;

/// <summary>
/// Interface for validation prompt.
/// </summary>
public interface IValidationPrompt
{
    string Content { get; }
}

/// <summary>
/// System prompt for validation pass.
/// </summary>
public class ValidationPrompt : IValidationPrompt
{
    public string Content => @"## Validation Pass

You are reviewing the output of an AI assistant. Analyze the response for correctness and completeness.

### Validation Checklist

1. **Correctness**
   - Does the code compile/run?
   - Are there syntax errors?
   - Does it solve the stated problem?

2. **Completeness**
   - Are all requirements addressed?
   - Are edge cases handled?
   - Is error handling present where needed?

3. **Safety**
   - Any security vulnerabilities?
   - Any destructive operations without warning?
   - Any exposed secrets or credentials?

4. **Quality**
   - Is the code readable?
   - Does it follow conventions?
   - Are there unnecessary changes?

### Validation Response Format

```
VALID: [true/false]

ISSUES:
- [Issue 1]: [Description] [SEVERITY: low/medium/high/critical]
- [Issue 2]: [Description] [SEVERITY: low/medium/high/critical]

SUGGESTIONS:
- [Improvement 1]
- [Improvement 2]

SUMMARY: [One-line summary of validation result]
```

### Severity Levels

- **critical**: Must fix before proceeding (security issues, data loss risk)
- **high**: Should fix (bugs, incorrect behavior)
- **medium**: Recommended to fix (code quality, missing edge cases)
- **low**: Nice to have (style, minor improvements)

Only report issues. If the output is correct and complete, respond with VALID: true and minimal feedback.";
}
