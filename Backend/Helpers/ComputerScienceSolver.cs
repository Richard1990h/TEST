using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LittleHelperAI.Backend.Helpers;

public static class ComputerScienceSolver
{
    public static bool LooksLikeCsQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var q = input.ToLowerInvariant();
        return q.Contains("binary") || q.Contains("hex") || q.Contains("decimal") || q.Contains("base64") ||
               q.Contains("ascii") || q.Contains("unicode") || q.Contains("utf") || q.Contains("sha") || q.Contains("md5") ||
               q.Contains("big o") || q.Contains("time complexity") || q.Contains("space complexity");
    }

    /// <summary>
    /// Deterministic CS utilities (no LLM): base conversions, hashing, base64.
    /// Returns null when it can't confidently answer.
    /// </summary>
    public static string? TrySolve(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var q = input.Trim();
        var lowered = q.ToLowerInvariant();

        // --- Base conversions ---
        // Examples:
        //  "convert 1010 to decimal"
        //  "0b1010 to hex"
        //  "255 in binary"
        var convertMatch = Regex.Match(lowered,
            @"(?:convert\s+)?(?<value>0b[01]+|0x[0-9a-f]+|[01]{2,}|\d{1,18})\s*(?:to|in)\s+(?<to>binary|bin|decimal|dec|hex|hexadecimal)");

        if (convertMatch.Success)
        {
            var raw = convertMatch.Groups["value"].Value;
            var to = convertMatch.Groups["to"].Value;

            if (!TryParseInteger(raw, out var number)) return null;
            return FormatConversion(number, to);
        }

        // Direct forms: "binary of 42", "hex of 255"
        var ofMatch = Regex.Match(lowered, @"(?<to>binary|bin|decimal|dec|hex|hexadecimal)\s+of\s+(?<value>\d{1,18})");
        if (ofMatch.Success)
        {
            var to = ofMatch.Groups["to"].Value;
            if (long.TryParse(ofMatch.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return FormatConversion(n, to);
        }

        // --- Base64 encode/decode ---
        if (lowered.StartsWith("base64 encode"))
        {
            var payload = q.Substring("base64 encode".Length).Trim();
            if (payload.Length == 0) return null;
            var bytes = Encoding.UTF8.GetBytes(payload);
            return $"🔐 Base64: {Convert.ToBase64String(bytes)}";
        }

        if (lowered.StartsWith("base64 decode"))
        {
            var payload = q.Substring("base64 decode".Length).Trim();
            if (payload.Length == 0) return null;
            try
            {
                var bytes = Convert.FromBase64String(payload);
                return $"🔓 Decoded: {Encoding.UTF8.GetString(bytes)}";
            }
            catch { return null; }
        }

        // --- Hashing ---
        // Examples:
        // "sha256 hello"
        // "md5 test"
        var hashMatch = Regex.Match(lowered, @"^(?<alg>sha256|sha1|md5)\s+(?<text>.+)$");
        if (hashMatch.Success)
        {
            var alg = hashMatch.Groups["alg"].Value;
            var text = hashMatch.Groups["text"].Value;
            var bytes = Encoding.UTF8.GetBytes(text);

            byte[] hash = alg switch
            {
                "sha256" => SHA256.HashData(bytes),
                "sha1" => SHA1.HashData(bytes),
                "md5" => MD5.HashData(bytes),
                _ => Array.Empty<byte>()
            };

            if (hash.Length == 0) return null;
            return $"#️⃣ {alg.ToUpperInvariant()}: {Convert.ToHexString(hash).ToLowerInvariant()}";
        }

        return null;
    }

    private static bool TryParseInteger(string raw, out long value)
    {
        raw = raw.Trim().ToLowerInvariant();
        if (raw.StartsWith("0b"))
        {
            var s = raw[2..];
            try { value = Convert.ToInt64(s, 2); return true; } catch { value = 0; return false; }
        }
        if (raw.StartsWith("0x"))
        {
            var s = raw[2..];
            try { value = Convert.ToInt64(s, 16); return true; } catch { value = 0; return false; }
        }
        if (Regex.IsMatch(raw, "^[01]{2,}$"))
        {
            try { value = Convert.ToInt64(raw, 2); return true; } catch { value = 0; return false; }
        }
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatConversion(long number, string to)
    {
        to = to.ToLowerInvariant();
        return to switch
        {
            "binary" or "bin" => $"💻 {number} in binary is {Convert.ToString(number, 2)}",
            "hex" or "hexadecimal" => $"💻 {number} in hex is 0x{number:x}",
            "decimal" or "dec" => $"💻 Decimal value is {number}",
            _ => $"💻 {number}"
        };
    }
}
