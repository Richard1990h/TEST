namespace LittleHelperAI.Backend.Models;

// ===============================
// COMPILER DIAGNOSTICS
// ===============================
public sealed class CompilerIssue
{
    public string Severity { get; set; } = "Info"; // Error|Warning|Info
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

// ===============================
// STATIC ANALYSIS ISSUES
// ===============================
public sealed class StaticIssue
{
    public string Severity { get; set; } = "Info"; // Critical|Warning|Info
    public string RuleId { get; set; } = "";
    public string Message { get; set; } = "";
    public int Line { get; set; }
}

// ===============================
// CODE FIX RESULT (EXTENDED)
// ===============================
public sealed class CodeFixResult
{
    // ===== EXISTING (UNCHANGED) =====
    public bool Fixed { get; set; }
    public string Language { get; set; } = "code";
    public string OriginalCode { get; set; } = "";
    public string FixedCode { get; set; } = "";
    public List<string> Notes { get; set; } = new();
    public List<CompilerIssue> CompilerDiagnostics { get; set; } = new();
    public List<StaticIssue> StaticIssues { get; set; } = new();
    public string Diff { get; set; } = "";
    public bool Compiled { get; set; }
    public bool StaticClean { get; set; }
    public bool UsedLLM { get; set; }

    // ===== NEW (SAFE ADDITIONS) =====

    /// <summary>
    /// Confidence score (0.0 – 1.0) indicating how reliable the fix is
    /// Deterministic fixes should be high (0.9+), LLM fixes lower.
    /// </summary>
    public double Confidence { get; set; } = 0.0;

    /// <summary>
    /// Human-readable explanation of what was fixed and why
    /// (ChatGPT-style explanation)
    /// </summary>
    public string? Explanation { get; set; }

    /// <summary>
    /// Fixed code WITH inline comments added (optional)
    /// Original FixedCode remains untouched
    /// </summary>
    public string? FixedCodeWithComments { get; set; }

    /// <summary>
    /// Suggested unit tests to validate the fix
    /// (language-agnostic strings)
    /// </summary>
    public List<string> SuggestedTests { get; set; } = new();

    /// <summary>
    /// True if fix was deterministic (compiler/static rules)
    /// False if AI-assisted
    /// </summary>
    public bool DeterministicFix { get; set; }
}
