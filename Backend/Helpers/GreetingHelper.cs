using System;
using LittleHelperAI.Backend.Utils;

namespace LittleHelperAI.Backend.Helpers
{
    public static class GreetingHelper
    {
        private static readonly string[] Greetings = {
            "hello", "hi", "hey", "hiya", "helloo", "hii", "yo", "sup", "what's up", "heya",
            "good morning", "good afternoon", "good evening", "good night", "morning", "evening"
        };

        private static readonly string[] RandomGreetings =
        {
            "👋 Hello there! How can I help you today?",
            "Hey! What can I do for you?",
            "Hi 🙂 Need a hand with anything?",
            "Hello! Ready when you are.",
            "Hey there! Fire away."
        };

        public static bool IsGreeting(string input)
        {
            return FuzzyMatch.ContainsSimilarPhrase(input, Greetings, 2);
        }

        public static string TrySolve(string input)
        {
            var lowered = input.ToLowerInvariant();

            if (lowered.Contains("morning"))
                return "🌅 Good morning! Ready to get started?";
            if (lowered.Contains("afternoon"))
                return "☀️ Good afternoon! What can I do for you?";
            if (lowered.Contains("evening"))
                return "🌇 Good evening! How may I assist you?";
            if (lowered.Contains("night"))
                return "🌙 Good night! Let me know if you need anything before you rest.";

            return GetRandomGreeting();
        }

        // ✅ ADD THIS — fixes compiler error
        public static string GetRandomGreeting()
        {
            return RandomGreetings[Random.Shared.Next(RandomGreetings.Length)];
        }
    }
}
