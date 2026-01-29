using System;
using System.Globalization;
using LittleHelperAI.Backend.Utils;

namespace LittleHelperAI.Backend.Helpers
{
    public static class DateTimeHelper
    {
        private static readonly string[] Phrases =
        {
            "what time", "current time", "time now", "tell me the time",
            "what's the date", "today's date", "current date",
            "date today", "time in", "time at", "what time is it in", "clock in"
        };

        private static readonly Dictionary<string, string> TimeZones = new()
        {
            { "new york", "Eastern Standard Time" },
            { "london", "GMT Standard Time" },
            { "tokyo", "Tokyo Standard Time" },
            { "sydney", "AUS Eastern Standard Time" },
            { "berlin", "W. Europe Standard Time" },
            { "paris", "Romance Standard Time" },
            { "beijing", "China Standard Time" },
            { "delhi", "India Standard Time" },
            { "moscow", "Russian Standard Time" },
        };

        public static bool IsDateTimeQuery(string input)
        {
            return FuzzyMatch.ContainsSimilarPhrase(input, Phrases, 2);
        }

        public static string TrySolve(string input)
        {
            var lowered = input.ToLowerInvariant();

            // 🌍 Timezone city lookup
            foreach (var (city, zoneId) in TimeZones)
            {
                if (lowered.Contains(city))
                {
                    try
                    {
                        var tz = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                        var cityTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz);
                        return $"🕒 Current time in {CultureInfo.CurrentCulture.TextInfo.ToTitleCase(city)} is {cityTime:hh:mm tt}";
                    }
                    catch
                    {
                        return $"⚠️ I couldn't get the time for {city}.";
                    }
                }
            }

            if (lowered.Contains("time"))
                return $"⏰ Current Local Time: {DateTime.Now:hh:mm tt}";

            if (lowered.Contains("date"))
                return $"📅 Today's Date: {DateTime.Today:D}";

            return string.Empty;
        }

        // ✅ ADD THIS — fixes compiler error
        public static string GetCurrentTime()
        {
            return $"⏰ Current Local Time: {DateTime.Now:hh:mm tt}";
        }
    }
}
