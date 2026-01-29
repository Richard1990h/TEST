using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace LittleHelperAI.Backend.Helpers
{
    public static class CodeUtils
    {
        private static readonly string[] CodeIndicators = {
            "using ", "namespace", "class ", "public ", "void ", "Console.", "return", ";", "var ", "new ",
            "def ", "print(", "import ", "from ", "elif ", "except", "#",
            "function", "let ", "const ", "=>", "console.log",
            "#include", "int main(", "std::", "->",
            "<?php", "echo ", "$", "=>",
            "fn main", "println!", "match", "impl",
            "package main", "func ", "defer",
            "<script>", "<html>", "<div>", "</", "<?xml", "<!DOCTYPE",
            "{", "}", "[", "]", ":", "=", "==", "!=", "+=", "-=", "*=", "/="
        };

        public static bool LooksLikeCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var matchCount = CodeIndicators.Count(i =>
                input.IndexOf(i, StringComparison.OrdinalIgnoreCase) >= 0
            );

            // If a large portion of tokens look like code, it's code
            var lines = input.Split('\n');
            var isMultiLineCode = lines.Length > 2 && matchCount > 1;
            
            // Check for indentation patterns typical of code
            var hasCodeIndentation = lines.Any(l => l.StartsWith("    ") || l.StartsWith("\t"));

            // Simple confidence threshold
            return matchCount >= 2 || isMultiLineCode || LooksLikeJsonOrYaml(input) || hasCodeIndentation;
        }

        private static bool LooksLikeJsonOrYaml(string input)
        {
            var trimmed = input.TrimStart();
            return trimmed.StartsWith("{") || trimmed.StartsWith("[") || (input.Contains(":") && input.Contains("-"));
        }

        public static string DetectLanguage(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "code";

            input = input.ToLowerInvariant();

            if (input.Contains("using ") || input.Contains("console.") || input.Contains("namespace") || input.Contains(".cs"))
                return "C#";
            if (input.Contains("def ") || input.Contains("print(") || input.Contains("import ") || input.Contains(".py"))
                return "Python";
            if (input.Contains("public static void main") || input.Contains("system.out.println") || input.Contains(".java"))
                return "Java";
            if (input.Contains("function") || input.Contains("console.log") || input.Contains("let ") || input.Contains("const ") || input.Contains(".js"))
                return "JavaScript";
            if (input.Contains("#include") || input.Contains("int main(") || input.Contains(".cpp") || input.Contains(".h"))
                return "C++";
            if (input.Contains("fn main") || input.Contains("println!") || input.Contains(".rs"))
                return "Rust";
            if (input.Contains("func ") || input.Contains("package main") || input.Contains(".go"))
                return "Go";
            if (input.Contains("<?php") || input.Contains("echo ") || input.Contains("->") || input.Contains(".php"))
                return "PHP";
            if (input.Contains("begin") && input.Contains("end;") || input.Contains("select ") || input.Contains(".sql"))
                return "SQL";
            if (input.Contains("val ") || input.Contains("fun ") || input.Contains(".kt"))
                return "Kotlin";
            if (input.Contains("swift ") || input.Contains("let ") && input.Contains(":") && input.Contains("->"))
                return "Swift";
            if (input.Contains("<html") || input.Contains("<div") || input.Contains("<script>") || input.Contains("<!doctype"))
                return "HTML";
            if (input.Trim().StartsWith("{") || input.Trim().StartsWith("["))
                return "JSON";
            if (input.Contains(":") && input.Contains("-"))
                return "YAML";

            return "code";
        }
    }
}
