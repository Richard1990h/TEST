namespace LittleHelperAI.Shared.Utils;

public static class CodeUtils
{
    private static readonly string[] CodeIndicators = new[]
    {
        "public ", "class ", "def ", "import ", "function ", "var ", "let ", "const ",
        "#include", "System.", "Console.", "{", "}", ";", "print(", "if(", "=>", "return ",
        "fn ", "package ", "fmt.", "use ", "<?php", "echo ", "println!", "match "
    };

    public static bool LooksLikeCode(string input)
    {
        return CodeIndicators.Any(i => input.Contains(i, StringComparison.OrdinalIgnoreCase));
    }

    public static string DetectLanguage(string input)
    {
        if (input.Contains("def ") || input.Contains("import ")) return "Python";
        if (input.Contains("System.") || input.Contains("public class")) return "C#";
        if (input.Contains("console.log") || input.Contains("function")) return "JavaScript";
        if (input.Contains("fn ") || input.Contains("println!")) return "Rust";
        if (input.Contains("<?php") || input.Contains("echo ")) return "PHP";
        if (input.Contains("package ") && input.Contains("fmt.")) return "Go";

        return "Unknown";
    }
}
