using System.Text.RegularExpressions;

namespace LittleHelperAI.Backend.Helpers
{
    public static class TitleAnalyzer
    {
        public static string GenerateTitle(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Untitled";

            var keywords = new[] { "error", "bug", "fix", "exception", "problem", "issue", "calculate", "solve", "how to", "code", "convert" };

            foreach (var keyword in keywords)
            {
                if (message.ToLower().Contains(keyword))
                {
                    return CapitalizeFirst(keyword) + " Request";
                }
            }

            if (CodeUtils.LooksLikeCode(message))
            {
                var language = CodeUtils.DetectLanguage(message);
                return $"Code Review ({language})";
            }

            if (Regex.IsMatch(message, @"[\d\+\-\*/=]"))
            {
                return "Math Problem";
            }

            return message.Length > 30 ? message[..30] + "..." : message;
        }

        private static string CapitalizeFirst(string input)
        {
            return char.ToUpper(input[0]) + input.Substring(1);
        }
    }
}
