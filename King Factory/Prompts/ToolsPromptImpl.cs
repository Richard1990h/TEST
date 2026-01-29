namespace LittleHelperAI.KingFactory.Prompts;

/// <summary>
/// Implementation of tools prompt.
/// </summary>
public sealed class ToolsPromptImpl : IToolsPrompt
{
    public string Content => @"## Available Tools

write_file
- Create/overwrite a file.
- args: path (string), content (string)

read_file
- Read a file.
- args: path (string)

list_files
- List a directory.
- args: path (string), recursive (bool, optional)

run_command
- Run a shell command.
- args: command (string), workingDirectory (string, optional), timeout (int, optional)

fetch
- HTTP request.
- args: url (string), method (string, optional), headers (object, optional), body (string, optional)

Tool call format (exact):
```tool
{
  ""tool"": ""tool_name"",
  ""arguments"": { ""param"": ""value"" }
}
```";
}
