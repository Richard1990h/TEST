using System;

namespace LittleHelperAI.Shared.Models;

/// <summary>
/// Support ticket / complaint submitted by users
/// </summary>
public sealed class SupportTicket
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "open"; // open, in_progress, resolved, closed
    public string Priority { get; set; } = "normal"; // low, normal, high, urgent
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? AssignedAdminId { get; set; }
}

/// <summary>
/// Reply/message in a support ticket thread
/// </summary>
public sealed class SupportTicketReply
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int SenderId { get; set; }
    public bool IsAdminReply { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO for displaying tickets with user info
/// </summary>
public sealed class SupportTicketDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? AssignedAdminId { get; set; }
    public int ReplyCount { get; set; }
}

/// <summary>
/// DTO for replies with sender info
/// </summary>
public sealed class SupportTicketReplyDto
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = "";
    public bool IsAdminReply { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
