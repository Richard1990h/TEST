namespace LittleHelperAI.Shared.Models;

/// <summary>
/// Request model for code analysis
/// </summary>
public sealed class AnalyzeFileRequest
{
    /// <summary>
    /// The file name (used for language detection)
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// The source code to analyze
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Optional preset: Safe / Aggressive / AiAssist
    /// </summary>
    public string Preset { get; set; } = "Safe";
    
    /// <summary>
    /// Whether to enable LLM-based fixes for non-C# languages.
    /// Defaults to true when not specified.
    /// </summary>
    public bool? EnableLlmFixes { get; set; }



    public bool StreamExplanation { get; set; } = false;
    public int? ChatId { get; set; }

}
