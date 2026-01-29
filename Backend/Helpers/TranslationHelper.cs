using System;
using System.Linq;
using System.Text.RegularExpressions;
using LittleHelperAI.Backend.Utils;

namespace LittleHelperAI.Backend.Helpers
{
    public static class TranslationHelper
    {
        private static readonly string[] TranslationPhrases = {
            "translate", "how do you say", "how to say", "what's this in", "in spanish", "in french", "into german", "what's the word for", "meaning in"
        };

        // ✅ Detect if the message is likely a translation request
        public static bool IsTranslationRequest(string input)
        {
            return FuzzyMatch.ContainsSimilarPhrase(input, TranslationPhrases, 2);
        }

        // ✅ Extract and simulate the translation
        public static string TrySolve(string input)
        {
            var lowered = input.ToLower();

            // Common patterns
            var patterns = new[]
            {
                @"(?:translate|how do you say|how to say|what'?s this in|what'?s the word for)?\s*'?([\w\s]+)'?\s+(?:to|in|into)\s+(\w+)",
                @"([\w\s]+)\s+(?:in|into)\s+(\w+)", // fallback: "hi in french"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(lowered, pattern);
                if (match.Success)
                {
                    string phrase = match.Groups[1].Value.Trim();
                    string lang = match.Groups[2].Value.Trim();
                    return $"🌍 '{phrase}' in {ToTitleCase(lang)} is: **{SimulateTranslation(phrase, lang)}**";
                }
            }

            return "🈳 I didn't catch what you want to translate and into which language. Try: `Translate 'hello' to Spanish`.";
        }

        // ✅ Fake translated output for supported demo languages
        private static string SimulateTranslation(string phrase, string lang)
        {
            return lang.ToLower() switch
            {
                "spanish" => $"[Simulado: {phrase}o]",
                "french" => $"[Simulé: le {phrase}]",
                "german" => $"[Simuliert: {phrase}en]",
                "italian" => $"[Simulato: {phrase}a]",
                "japanese" => $"[シミュレート: {phrase}です]",
                _ => $"[Simulated translation of '{phrase}']"
            };
        }

        private static string ToTitleCase(string word)
        {
            return string.IsNullOrWhiteSpace(word) ? word : char.ToUpper(word[0]) + word.Substring(1).ToLower();
        }
    }
}
