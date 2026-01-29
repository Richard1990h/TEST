namespace LittleHelperAI.Shared.Models
{
    public class ProjectGenerationRequest
    {
        public int UserId { get; set; }
        public string Description { get; set; } = "";
        public string Language { get; set; } = "C#";
        public string Framework { get; set; } = "";
        public bool IncludeTests { get; set; } = true;
        public bool IncludeDocumentation { get; set; } = true;
    }

    public class GeneratedProject
    {
        public bool Success { get; set; }
        public string ProjectName { get; set; } = "";
        public string Description { get; set; } = "";
        public List<GeneratedFile> Files { get; set; } = new();
        public List<GeneratedFolder> Folders { get; set; } = new();
        public string? Error { get; set; }
    }

    public class GeneratedFile
    {
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public string FileType { get; set; } = "";
    }

    public class GeneratedFolder
    {
        public string Path { get; set; } = "";
        public string Purpose { get; set; } = "";
    }
}
