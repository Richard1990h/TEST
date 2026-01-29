using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LittleHelperAI.Backend.Utils
{
    public static class FuzzyMatch
    {
        // Default human tolerance score
        private const int DEFAULT_THRESHOLD = 120;

        // Common slang / shorthand / lazy typing
        private static readonly Dictionary<string, string> SlangMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["u"] = "you",
                ["ur"] = "your",
                ["r"] = "are",
                ["pls"] = "please",
                ["plz"] = "please",
                ["thx"] = "thanks",
                ["ty"] = "thank you",
                ["idk"] = "i dont know",
                ["dont"] = "don't",
                ["cant"] = "can't",
                ["wont"] = "won't",
                ["im"] = "i am",
                ["respon"] = "respond",
                ["responf"] = "respond",
                ["talkin"] = "talking",
                ["goin"] = "going",
                ["calc"] = "calculate",
                ["maths"] = "math",
                ["hlp"] = "help"
            };

        // =====================================================
        // MAIN ENTRY (USED BY MathSolver, helpers, etc.)
        // =====================================================
        public static bool ContainsSimilarPhrase(
            string input,
            string[] phrases,
            int threshold = DEFAULT_THRESHOLD)
        {
            if (string.IsNullOrWhiteSpace(input) || phrases == null || phrases.Length == 0)
                return false;

            var inputWords = NormalizeAndTokenize(input);

            foreach (var phrase in phrases)
            {
                var phraseWords = NormalizeAndTokenize(phrase);
                int score = 0;

                foreach (var pw in phraseWords)
                {
                    foreach (var iw in inputWords)
                    {
                        score += CompareWords(iw, pw);

                        if (score >= threshold)
                            return true;
                    }
                }
            }

            return false;
        }

        // =====================================================
        // WORD COMPARISON (STACKED HUMAN HEURISTICS)
        // =====================================================
        private static int CompareWords(string a, string b)
        {
            if (a == b)
                return 120; // exact match

            if (a.StartsWith(b) || b.StartsWith(a))
                return 95; // partial (respon vs respond)

            if (NormalizeRepeats(a) == NormalizeRepeats(b))
                return 90; // heeellppp vs help

            if (Soundex(a) == Soundex(b))
                return 85; // phonetic match

            int d = DamerauLevenshtein(a, b);
            if (d == 1) return 80;
            if (d == 2) return 65;
            if (d == 3) return 50;

            return 0;
        }

        // =====================================================
        // NORMALIZATION
        // =====================================================
        private static List<string> NormalizeAndTokenize(string input)
        {
            return Regex.Matches(input.ToLowerInvariant(), @"\b[a-z']+\b")
                .Select(m => m.Value)
                .Select(ApplySlang)
                .Select(NormalizeRepeats)
                .ToList();
        }

        private static string ApplySlang(string word)
        {
            return SlangMap.TryGetValue(word, out var mapped) ? mapped : word;
        }

        private static string NormalizeRepeats(string word)
        {
            // heeellppp → help
            return Regex.Replace(word, @"(.)\1{2,}", "$1");
        }

        // =====================================================
        // PHONETIC MATCH (Soundex-style)
        // =====================================================
        private static string Soundex(string word)
        {
            if (string.IsNullOrEmpty(word))
                return "";

            word = word.ToLowerInvariant();

            char first = word[0];
            var map = new Dictionary<char, char>
            {
                ['b'] = '1',
                ['f'] = '1',
                ['p'] = '1',
                ['v'] = '1',
                ['c'] = '2',
                ['g'] = '2',
                ['j'] = '2',
                ['k'] = '2',
                ['q'] = '2',
                ['s'] = '2',
                ['x'] = '2',
                ['z'] = '2',
                ['d'] = '3',
                ['t'] = '3',
                ['l'] = '4',
                ['m'] = '5',
                ['n'] = '5',
                ['r'] = '6'
            };

            var result = first.ToString();
            char last = map.ContainsKey(first) ? map[first] : '0';

            foreach (var c in word.Skip(1))
            {
                char code = map.ContainsKey(c) ? map[c] : '0';
                if (code != last && code != '0')
                    result += code;
                last = code;
            }

            return result.PadRight(4, '0')[..4];
        }

        // =====================================================
        // ⚠️ KEEP THIS — PUBLIC API
        // Used by MathSolver & SpellCorrector
        // =====================================================
        public static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b.Length;
            if (string.IsNullOrEmpty(b)) return a.Length;

            int[,] d = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }

        // =====================================================
        // BETTER EDIT DISTANCE (PRIVATE)
        // Handles swapped letters: hlep → help
        // =====================================================
        private static int DamerauLevenshtein(string a, string b)
        {
            int lenA = a.Length;
            int lenB = b.Length;
            var d = new int[lenA + 1, lenB + 1];

            for (int i = 0; i <= lenA; i++) d[i, 0] = i;
            for (int j = 0; j <= lenB; j++) d[0, j] = j;

            for (int i = 1; i <= lenA; i++)
            {
                for (int j = 1; j <= lenB; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;

                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );

                    if (i > 1 && j > 1 &&
                        a[i - 1] == b[j - 2] &&
                        a[i - 2] == b[j - 1])
                    {
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + cost);
                    }
                }
            }

            return d[lenA, lenB];
        }
    }
}
