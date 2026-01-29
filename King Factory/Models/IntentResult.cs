namespace LittleHelperAI.KingFactory.Models;

/// <summary>
/// Result of intent classification.
/// </summary>
public class IntentResult
{
    /// <summary>
    /// Original message that was classified.
    /// </summary>
    public string OriginalMessage { get; set; } = string.Empty;

    /// <summary>
    /// Classified intent type.
    /// </summary>
    public IntentType Intent { get; set; } = IntentType.GeneralQuery;

    /// <summary>
    /// Category of the intent.
    /// </summary>
    public IntentCategory Category { get; set; } = IntentCategory.Information;

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Whether this intent requires tool usage.
    /// </summary>
    public bool RequiresTools { get; set; }

    /// <summary>
    /// Whether this intent requires planning.
    /// </summary>
    public bool RequiresPlan { get; set; }

    /// <summary>
    /// Whether this intent requires user confirmation.
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// Detected signals from the message.
    /// </summary>
    public List<string> DetectedSignals { get; set; } = new();

    /// <summary>
    /// Entities extracted from the message.
    /// </summary>
    public Dictionary<string, string> ExtractedEntities { get; set; } = new();

    /// <summary>
    /// Scope information.
    /// </summary>
    public ScopeInfo? Scope { get; set; }
}

/// <summary>
/// Specific intent types.
/// </summary>
public enum IntentType
{
    // Information intents
    GeneralQuery,
    Help,
    Search,
    Clarification,

    // Code intents
    CodeRead,
    CodeWrite,
    CodeEdit,
    CodeExplain,

    // File intents
    FileList,
    FileCreate,
    FileDelete,

    // System intents
    ShellCommand,

    // Planning intents
    Planning,

    // Feedback intents
    Confirmation,
    Rejection
}

/// <summary>
/// Categories of intents.
/// </summary>
public enum IntentCategory
{
    /// <summary>
    /// Information seeking intents.
    /// </summary>
    Information,

    /// <summary>
    /// Task execution intents.
    /// </summary>
    Task,

    /// <summary>
    /// Planning/strategy intents.
    /// </summary>
    Planning,

    /// <summary>
    /// User feedback intents.
    /// </summary>
    Feedback
}

/// <summary>
/// Scope information extracted from user request.
/// </summary>
public class ScopeInfo
{
    public List<string> AffectedFiles { get; set; } = new();
    public List<string> AffectedDirectories { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public string? Language { get; set; }
    public string? Framework { get; set; }
}
