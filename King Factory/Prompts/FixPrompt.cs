namespace LittleHelperAI.KingFactory.Prompts;

/// <summary>
/// Interface for fix-only prompt.
/// </summary>
public interface IFixPrompt
{
    string Content { get; }
}

/// <summary>
/// Prompt for targeted fixes.
/// </summary>
public sealed class FixPrompt : IFixPrompt
{
    public string Content =>
@"## Fix Mode

Fix only the reported issues while keeping the original task intact.
Return ONLY the corrected code. Do NOT add explanations or markdown.";
}
