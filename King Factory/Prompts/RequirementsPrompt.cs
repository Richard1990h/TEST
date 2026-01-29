namespace LittleHelperAI.KingFactory.Prompts;

/// <summary>
/// Interface for requirements extraction prompt.
/// </summary>
public interface IRequirementsPrompt
{
    string Content { get; }
}

/// <summary>
/// Prompt for extracting explicit requirements.
/// </summary>
public sealed class RequirementsPrompt : IRequirementsPrompt
{
    public string Content =>
@"Extract requirements from the user's request without changing the task.
Return ONLY valid JSON with the following schema:

{
  ""task"": ""string"",
  ""language"": ""string or empty"",
  ""platform"": ""browser|node|desktop|mobile|server|unknown"",
  ""runtime"": ""string or empty"",
  ""inputs"": [""string""],
  ""outputs"": [""string""],
  ""constraints"": [""string""],
  ""missing"": [""string""]
}

Rules:
- Do not invent requirements.
- If a field is unknown, use ""unknown"" or an empty string/array.
- The ""missing"" array must list required details that are not specified.";
}
