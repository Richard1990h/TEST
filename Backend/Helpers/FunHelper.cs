using LittleHelperAI.Backend.Utils;

namespace LittleHelperAI.Backend.Helpers
{
    public static class FunHelper
    {
        private static readonly string[] Keywords = {
            "joke", "laugh", "funny", "something fun", "cheer me up", "i'm bored",
            "bored", "i'm sad", "make me smile", "make me laugh", "tell me a joke",
            "lol", "giggle"
        };

        private static readonly string[] Jokes = {
            "Why do programmers hate nature? It has too many bugs.",
            "A SQL query walks into a bar, walks up to two tables and asks: 'Can I join you?'",
            "To understand what recursion is, you must first understand recursion.",
            "Why was the JavaScript developer sad? Because he didn’t know how to 'null' his feelings.",
            "Why did the function return early? Because it had a return statement.",
            "Why do Java developers wear glasses? Because they can’t C#."
        };

        public static bool IsJokeRequest(string input)
        {
            return FuzzyMatch.ContainsSimilarPhrase(input, Keywords, 2);
        }

        public static string TrySolve(string input)
        {
            return "😂 " + GetJoke();
        }

        // ✅ ADD THIS — fixes compiler error
        public static string GetJoke()
        {
            var rnd = Random.Shared;
            return Jokes[rnd.Next(Jokes.Length)];
        }
    }
}
