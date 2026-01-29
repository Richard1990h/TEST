// File: Prompts/SystemPrompts.cs
using System.Text;

namespace LittleHelperAI.KingFactory.Prompts;

/// <summary>
/// Interface for accessing system prompts.
/// </summary>
public interface ISystemPrompts
{
    string GetCorePrompt();
    string GetPlanningPrompt();
    string GetToolsPrompt();
    string GetValidationPrompt();
    string GetStreamingPrompt();
    string GetCodePrompt();
    string GetFixPrompt();

    /// <summary>
    /// Build a composed system prompt. Keep planning OFF unless explicitly enabled.
    /// </summary>
    string Build(PromptBuildOptions? options = null);
}

public sealed class PromptBuildOptions
{
    public bool IncludeTools { get; init; } = false;
    public bool IncludePlanning { get; init; } = false; // should be opt-in only
    public bool IncludeCodeMode { get; init; } = false; // strict code output
    public bool IncludeFixMode { get; init; } = false;  // strict fix output
    public bool IncludeValidation { get; init; } = false;
}

/// <summary>
/// Central repository for all system prompts.
/// </summary>
public sealed class SystemPrompts : ISystemPrompts
{
    private readonly ICorePrompt _corePrompt;
    private readonly IPlanningPrompt _planningPrompt;
    private readonly IToolsPrompt _toolsPrompt;
    private readonly IValidationPrompt _validationPrompt;
    private readonly IStreamingPrompt _streamingPrompt;
    private readonly ICodePrompt _codePrompt;
    private readonly IFixPrompt _fixPrompt;

    public SystemPrompts(
        ICorePrompt corePrompt,
        IPlanningPrompt planningPrompt,
        IToolsPrompt toolsPrompt,
        IValidationPrompt validationPrompt,
        IStreamingPrompt streamingPrompt,
        ICodePrompt codePrompt,
        IFixPrompt fixPrompt)
    {
        _corePrompt = corePrompt;
        _planningPrompt = planningPrompt;
        _toolsPrompt = toolsPrompt;
        _validationPrompt = validationPrompt;
        _streamingPrompt = streamingPrompt;
        _codePrompt = codePrompt;
        _fixPrompt = fixPrompt;
    }

    public string GetCorePrompt() => _corePrompt.Content;
    public string GetPlanningPrompt() => _planningPrompt.Content;
    public string GetToolsPrompt() => _toolsPrompt.Content;
    public string GetValidationPrompt() => _validationPrompt.Content;
    public string GetStreamingPrompt() => _streamingPrompt.Content;
    public string GetCodePrompt() => _codePrompt.Content;
    public string GetFixPrompt() => _fixPrompt.Content;

    public string Build(PromptBuildOptions? options = null)
    {
        options ??= new PromptBuildOptions();

        var sb = new StringBuilder();
        sb.AppendLine(GetCorePrompt());

        // Strict modes should be mutually exclusive; if both set, Fix wins.
        if (options.IncludeFixMode)
            sb.AppendLine().AppendLine(GetFixPrompt());
        else if (options.IncludeCodeMode)
            sb.AppendLine().AppendLine(GetCodePrompt());

        if (options.IncludePlanning)
            sb.AppendLine().AppendLine(GetPlanningPrompt());

        if (options.IncludeTools)
            sb.AppendLine().AppendLine(GetToolsPrompt());

        if (options.IncludeValidation)
            sb.AppendLine().AppendLine(GetValidationPrompt());

        return sb.ToString();
    }
}

public enum PromptType
{
    Core,
    Planning,
    Tools,
    Validation,
    Streaming,
    Code,
    Fix
}
