namespace LittleHelperAI.Frontend.Pages.Chat.Models;

public sealed class ProjectStatus
{
    public string Title { get; init; } = string.Empty;
    public int Progress { get; init; }
    public string CurrentStep { get; init; } = string.Empty;
}
