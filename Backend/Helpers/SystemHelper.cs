using System.Linq;
using LittleHelperAI.Backend.Utils;

namespace LittleHelperAI.Backend.Helpers
{
    public static class SystemHelper
    {
        private static readonly string[] SystemChecks = {
            "are you online", "are you working", "system status", "system check", "are you there",
            "are u there", "you there", "you alive", "still awake", "wake up", "you ok", "hello?", "ping"
        };

        public static bool IsSystemCheck(string input)
        {
            return FuzzyMatch.ContainsSimilarPhrase(input, SystemChecks, 2);
        }

        public static string TrySolve(string input)
        {
            var lowered = input.ToLower();

            if (lowered.Contains("alive") || lowered.Contains("wake"))
                return "🧠 I'm wide awake and ready!";

            if (lowered.Contains("ok") || lowered.Contains("you there"))
                return "👋 Yes, I’m here and listening!";

            if (lowered.Contains("ping"))
                return "📡 Pong! I'm responsive.";

            return "✅ I'm online and ready to help!";
        }
    }
}
