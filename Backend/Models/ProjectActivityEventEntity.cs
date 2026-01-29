using System;

namespace LittleHelperAI.Backend.Models;

/// <summary>
/// Persisted, unified activity stream event for Analyzer / Factory / Chat pipelines.
/// Additive-only entity used to render the mini Activity panel next to project output.
/// </summary>
public sealed class ProjectActivityEventEntity
{
    public long Id { get; set; }

    /// <summary>
    /// Project creation session id (Guid "N" format).
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    public int UserId { get; set; }

    /// <summary>
    /// "Analyzer" | "Factory" | "Chat"
    /// </summary>
    public string Source { get; set; } = "Factory";

    /// <summary>
    /// "Info" | "Warn" | "Error" | "Success"
    /// </summary>
    public string Level { get; set; } = "Info";

    /// <summary>
    /// Short phase key, e.g. FACTORY_CREATE / FIXING_FILES / BUILD / FALLBACK.
    /// </summary>
    public string Phase { get; set; } = "";

    /// <summary>
    /// Markdown-safe message for UI.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional JSON payload for structured error blocks.
    /// </summary>
    public string? DetailsJson { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
