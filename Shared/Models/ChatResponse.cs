namespace LittleHelperAI.Shared.Models
{
    public class ChatResponse
    {
        public string Message { get; set; } = "";
        public string Type { get; set; } = "text";
        public string? Language { get; set; }
        public string? Code { get; set; }
        public string? Diff { get; set; }
        public List<string>? Notes { get; set; }
        public string? Explanation { get; set; }
        public List<string>? SuggestedTests { get; set; }
        public double Confidence { get; set; } = 0.0;
        public bool Compiled { get; set; } = false;
        public double UsedCredits { get; set; } = 0.0;
        public double CreditsLeft { get; set; } = 0.0;
    }
}
