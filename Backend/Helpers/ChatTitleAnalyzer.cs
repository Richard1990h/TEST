namespace LittleHelperAI.Backend.Helpers
{
    public static class ChatTitleAnalyzer
    {
        public static string GenerateTitle(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Untitled";

            string lower = message.ToLowerInvariant();

            if (lower.Contains("error") || lower.Contains("exception"))
                return "Debugging Issue";
            if (lower.Contains("weather"))
                return "Weather Info";
            if (lower.Contains("sort") || lower.Contains("array") || lower.Contains("loop"))
                return "Algorithm Help";
            if (lower.Contains("sql") || lower.Contains("database"))
                return "Database Query";
            if (lower.Contains("login") || lower.Contains("auth"))
                return "Authentication Help";
            if (lower.Contains("math") || lower.Any(char.IsDigit) && (lower.Contains("+") || lower.Contains("-") || lower.Contains("*") || lower.Contains("/")))
                return "Math Problem";
            if (lower.Contains("write") || lower.Contains("grammar") || lower.Contains("essay"))
                return "Writing Assistance";
            if (lower.Contains("bug") || lower.Contains("fix"))
                return "Bug Fix Request";
            if (lower.Length < 20)
                return char.ToUpper(lower[0]) + lower[1..]; // Capitalize short phrases

            // Default: Use first few words
            var words = message.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Take(4)) + "...";
        }
    }
}
