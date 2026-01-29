namespace LittleHelperAI.Dashboard.Pages.Workbench;

/// <summary>
/// Represents a file in the workbench project.
/// </summary>
public class ProjectFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsModified { get; set; } = false;
}

/// <summary>
/// Represents a line of terminal output.
/// </summary>
public class TerminalLine
{
    public string Text { get; set; } = "";
    public bool IsError { get; set; } = false;
}
