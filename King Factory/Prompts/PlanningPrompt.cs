// File: Prompts/PlanningPrompt.cs
namespace LittleHelperAI.KingFactory.Prompts;

/// <summary>
/// Interface for planning prompt.
/// </summary>
public interface IPlanningPrompt
{
    string Content { get; }
}

/// <summary>
/// AAA production-grade planning prompt:
/// IMPORTANT: Use planning only when explicitly requested or when pipeline opts-in.
/// </summary>
public sealed class PlanningPrompt : IPlanningPrompt
{
    public string Content => """
## Planning Mode (Use Only When Explicitly Enabled)

Create a plan ONLY if:
- The system explicitly enabled planning, OR
- The user explicitly asked to plan.

If planning is not enabled, do NOT produce a plan—produce the requested output directly.

PLAN FORMAT:
GOAL: <one sentence>

STEPS:
1. <action> -> <expected result>
2. <action> -> <expected result>

RISKS:
- <risk> -> <mitigation>

VERIFICATION:
- <how to confirm success>

RULES:
- Keep steps minimal and actionable.
- Avoid speculative steps; prefer deterministic actions.
- Do not execute tool calls until execution is requested/approved by the system/user.
""";
}
