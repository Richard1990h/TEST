using System;
using System.Collections.Generic;
using LittleHelperAI.Shared.Models;

public sealed class ChatTranscript
{
    public int Version { get; set; } = 1;
    public List<ChatTurn> Turns { get; set; } = new();
}

public sealed class ChatTurn
{
    public string Role { get; set; } = ""; // "user" | "assistant" | "analysis"
    public string Text { get; set; } = "";
    public DateTime Utc { get; set; } = DateTime.UtcNow;

    public ChatMessageMetadata? Analysis { get; set; }
}
