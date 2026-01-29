using System.Data;
using LittleHelperAI.Backend.Services.Notifications;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Controllers;

/// <summary>
/// User-facing support ticket endpoints
/// </summary>
[ApiController]
[Authorize]
[Route("api/support")]
public sealed class SupportController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly NotificationStore _notifs;

    public SupportController(ApplicationDbContext db, NotificationStore notifs)
    {
        _db = db;
        _notifs = notifs;
    }

    /// <summary>
    /// Get current user's tickets
    /// </summary>
    [HttpGet("my-tickets")]
    public async Task<IActionResult> GetMyTickets(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.user_id, t.subject, t.message, t.status, t.priority, 
       t.created_at, t.updated_at, t.resolved_at, t.assigned_admin_id,
       (SELECT COUNT(*) FROM support_ticket_replies r WHERE r.ticket_id = t.id) as reply_count
FROM support_tickets t
WHERE t.user_id = @userId
ORDER BY t.created_at DESC
LIMIT 50;
";
        var pUid = cmd.CreateParameter(); pUid.ParameterName = "@userId"; pUid.Value = userId; cmd.Parameters.Add(pUid);

        var tickets = new List<SupportTicketDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            tickets.Add(new SupportTicketDto
            {
                Id = r.GetInt32(0),
                UserId = r.GetInt32(1),
                Subject = r.GetString(2),
                Message = r.GetString(3),
                Status = r.GetString(4),
                Priority = r.GetString(5),
                CreatedAt = r.GetDateTime(6),
                UpdatedAt = r.IsDBNull(7) ? null : r.GetDateTime(7),
                ResolvedAt = r.IsDBNull(8) ? null : r.GetDateTime(8),
                AssignedAdminId = r.IsDBNull(9) ? null : r.GetInt32(9),
                ReplyCount = r.GetInt32(10)
            });
        }

        return Ok(tickets);
    }

    /// <summary>
    /// Create a new support ticket
    /// </summary>
    public sealed class CreateTicketRequest
    {
        public string Subject { get; set; } = "";
        public string Message { get; set; } = "";
        public string Priority { get; set; } = "normal";
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest req, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Message))
            return BadRequest("Subject and message are required");

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO support_tickets (user_id, subject, message, status, priority, created_at)
VALUES (@userId, @subject, @message, 'open', @priority, UTC_TIMESTAMP());
SELECT LAST_INSERT_ID();
";
        var pUid = cmd.CreateParameter(); pUid.ParameterName = "@userId"; pUid.Value = userId; cmd.Parameters.Add(pUid);
        var pSubj = cmd.CreateParameter(); pSubj.ParameterName = "@subject"; pSubj.Value = req.Subject.Trim(); cmd.Parameters.Add(pSubj);
        var pMsg = cmd.CreateParameter(); pMsg.ParameterName = "@message"; pMsg.Value = req.Message.Trim(); cmd.Parameters.Add(pMsg);
        var pPri = cmd.CreateParameter(); pPri.ParameterName = "@priority"; pPri.Value = req.Priority ?? "normal"; cmd.Parameters.Add(pPri);

        var ticketId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));

        return Ok(new { ticketId, message = "Ticket created successfully" });
    }

    /// <summary>
    /// Get replies for a ticket (user can only see their own tickets)
    /// </summary>
    [HttpGet("ticket/{ticketId}/replies")]
    public async Task<IActionResult> GetTicketReplies(int ticketId, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Verify ownership
        await using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT user_id FROM support_tickets WHERE id = @ticketId";
        var pTid = verifyCmd.CreateParameter(); pTid.ParameterName = "@ticketId"; pTid.Value = ticketId; verifyCmd.Parameters.Add(pTid);
        var ownerIdObj = await verifyCmd.ExecuteScalarAsync(ct);
        if (ownerIdObj is null || Convert.ToInt32(ownerIdObj) != userId)
            return NotFound("Ticket not found");

        // Get replies
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT r.id, r.ticket_id, r.sender_id, u.username, r.is_admin_reply, r.message, r.created_at
FROM support_ticket_replies r
JOIN users u ON u.id = r.sender_id
WHERE r.ticket_id = @ticketId
ORDER BY r.created_at ASC;
";
        var pTid2 = cmd.CreateParameter(); pTid2.ParameterName = "@ticketId"; pTid2.Value = ticketId; cmd.Parameters.Add(pTid2);

        var replies = new List<SupportTicketReplyDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            replies.Add(new SupportTicketReplyDto
            {
                Id = reader.GetInt32(0),
                TicketId = reader.GetInt32(1),
                SenderId = reader.GetInt32(2),
                SenderName = reader.GetString(3),
                IsAdminReply = reader.GetInt32(4) == 1,
                Message = reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }

        return Ok(replies);
    }

    /// <summary>
    /// User replies to their ticket
    /// </summary>
    public sealed class ReplyRequest
    {
        public string Message { get; set; } = "";
    }

    [HttpPost("ticket/{ticketId}/reply")]
    public async Task<IActionResult> ReplyToTicket(int ticketId, [FromBody] ReplyRequest req, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest("Message is required");

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Verify ownership
        await using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT user_id FROM support_tickets WHERE id = @ticketId";
        var pTid = verifyCmd.CreateParameter(); pTid.ParameterName = "@ticketId"; pTid.Value = ticketId; verifyCmd.Parameters.Add(pTid);
        var ownerIdObj = await verifyCmd.ExecuteScalarAsync(ct);
        if (ownerIdObj is null || Convert.ToInt32(ownerIdObj) != userId)
            return NotFound("Ticket not found");

        // Insert reply
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO support_ticket_replies (ticket_id, sender_id, is_admin_reply, message, created_at)
VALUES (@ticketId, @senderId, 0, @message, UTC_TIMESTAMP());

UPDATE support_tickets SET updated_at = UTC_TIMESTAMP() WHERE id = @ticketId;
";
        var pTid2 = cmd.CreateParameter(); pTid2.ParameterName = "@ticketId"; pTid2.Value = ticketId; cmd.Parameters.Add(pTid2);
        var pSid = cmd.CreateParameter(); pSid.ParameterName = "@senderId"; pSid.Value = userId; cmd.Parameters.Add(pSid);
        var pMsg = cmd.CreateParameter(); pMsg.ParameterName = "@message"; pMsg.Value = req.Message.Trim(); cmd.Parameters.Add(pMsg);

        await cmd.ExecuteNonQueryAsync(ct);

        return Ok(new { message = "Reply sent" });
    }

    private int GetUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
