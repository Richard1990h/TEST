namespace LittleHelperAI.Shared.Models
{
    public class ProjectScanResult
    {
        public bool Success { get; set; }
        public string ProjectName { get; set; } = "";
        public string ProjectDescription { get; set; } = "";
        public string DetectedLanguage { get; set; } = "";
        public string DetectedFramework { get; set; } = "";
        public List<string> Technologies { get; set; } = new();
        public ProjectStructure Structure { get; set; } = new();
        public string? Error { get; set; }
    }

    public class ProjectStructure
    {
        public int TotalFiles { get; set; }
        public int TotalDirectories { get; set; }
        public Dictionary<string, int> FilesByType { get; set; } = new();
    }
}
