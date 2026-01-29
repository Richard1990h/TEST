using System.Data;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Backend.Services.Notifications;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Controllers;

/// <summary>
/// Admin support ticket management
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/support")]
public sealed class AdminSupportController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notifs;
    private readonly AdminAuditLogger _audit;

    public AdminSupportController(ApplicationDbContext db, NotificationService notifs, AdminAuditLogger audit)
    {
        _db = db;
        _notifs = notifs;
        _audit = audit;
    }

    /// <summary>
    /// Get all tickets with filtering
    /// </summary>
    [HttpGet("tickets")]
    public async Task<IActionResult> GetAllTickets(
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] int take = 100,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Build WHERE clause
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) where.Add("t.status = @status");
        if (!string.IsNullOrWhiteSpace(priority)) where.Add("t.priority = @priority");
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        // Count total
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM support_tickets t {whereClause}";
        if (!string.IsNullOrWhiteSpace(status))
        {
            var p = countCmd.CreateParameter(); p.ParameterName = "@status"; p.Value = status; countCmd.Parameters.Add(p);
        }
        if (!string.IsNullOrWhiteSpace(priority))
        {
            var p = countCmd.CreateParameter(); p.ParameterName = "@priority"; p.Value = priority; countCmd.Parameters.Add(p);
        }
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        // Get tickets
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT t.id, t.user_id, u.username, u.email, t.subject, t.message, t.status, t.priority, 
       t.created_at, t.updated_at, t.resolved_at, t.assigned_admin_id,
       (SELECT COUNT(*) FROM support_ticket_replies r WHERE r.ticket_id = t.id) as reply_count
FROM support_tickets t
JOIN users u ON u.id = t.user_id
{whereClause}
ORDER BY 
    CASE t.priority WHEN 'urgent' THEN 1 WHEN 'high' THEN 2 WHEN 'normal' THEN 3 ELSE 4 END,
    t.created_at DESC
LIMIT @take OFFSET @skip;
";
        if (!string.IsNullOrWhiteSpace(status))
        {
            var p = cmd.CreateParameter(); p.ParameterName = "@status"; p.Value = status; cmd.Parameters.Add(p);
        }
        if (!string.IsNullOrWhiteSpace(priority))
        {
            var p = cmd.CreateParameter(); p.ParameterName = "@priority"; p.Value = priority; cmd.Parameters.Add(p);
        }
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "@take"; pTake.Value = Math.Clamp(take, 1, 500); cmd.Parameters.Add(pTake);
        var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "@skip"; pSkip.Value = Math.Max(skip, 0); cmd.Parameters.Add(pSkip);

        var tickets = new List<SupportTicketDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tickets.Add(new SupportTicketDto
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                UserName = reader.GetString(2),
                UserEmail = reader.GetString(3),
                Subject = reader.GetString(4),
                Message = reader.GetString(5),
                Status = reader.GetString(6),
                Priority = reader.GetString(7),
                CreatedAt = reader.GetDateTime(8),
                UpdatedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                ResolvedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                AssignedAdminId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                ReplyCount = reader.GetInt32(12)
            });
        }

        return Ok(new { total, items = tickets });
    }

    /// <summary>
    /// Get single ticket with all replies
    /// </summary>
    [HttpGet("ticket/{ticketId}")]
    public async Task<IActionResult> GetTicketDetails(int ticketId, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Get ticket
        await using var ticketCmd = conn.CreateCommand();
        ticketCmd.CommandText = @"
SELECT t.id, t.user_id, u.username, u.email, t.subject, t.message, t.status, t.priority, 
       t.created_at, t.updated_at, t.resolved_at, t.assigned_admin_id
FROM support_tickets t
JOIN users u ON u.id = t.user_id
WHERE t.id = @ticketId;
";
        var pTid = ticketCmd.CreateParameter(); pTid.ParameterName = "@ticketId"; pTid.Value = ticketId; ticketCmd.Parameters.Add(pTid);

        SupportTicketDto? ticket = null;
        await using var ticketReader = await ticketCmd.ExecuteReaderAsync(ct);
        if (await ticketReader.ReadAsync(ct))
        {
            ticket = new SupportTicketDto
            {
                Id = ticketReader.GetInt32(0),
                UserId = ticketReader.GetInt32(1),
                UserName = ticketReader.GetString(2),
                UserEmail = ticketReader.GetString(3),
                Subject = ticketReader.GetString(4),
                Message = ticketReader.GetString(5),
                Status = ticketReader.GetString(6),
                Priority = ticketReader.GetString(7),
                CreatedAt = ticketReader.GetDateTime(8),
                UpdatedAt = ticketReader.IsDBNull(9) ? null : ticketReader.GetDateTime(9),
                ResolvedAt = ticketReader.IsDBNull(10) ? null : ticketReader.GetDateTime(10),
                AssignedAdminId = ticketReader.IsDBNull(11) ? null : ticketReader.GetInt32(11)
            };
        }
        await ticketReader.CloseAsync();

        if (ticket is null) return NotFound();

        // Get replies
        await using var repliesCmd = conn.CreateCommand();
        repliesCmd.CommandText = @"
SELECT r.id, r.ticket_id, r.sender_id, u.username, r.is_admin_reply, r.message, r.created_at
FROM support_ticket_replies r
JOIN users u ON u.id = r.sender_id
WHERE r.ticket_id = @ticketId
ORDER BY r.created_at ASC;
";
        var pTid2 = repliesCmd.CreateParameter(); pTid2.ParameterName = "@ticketId"; pTid2.Value = ticketId; repliesCmd.Parameters.Add(pTid2);

        var replies = new List<SupportTicketReplyDto>();
        await using var repliesReader = await repliesCmd.ExecuteReaderAsync(ct);
        while (await repliesReader.ReadAsync(ct))
        {
            replies.Add(new SupportTicketReplyDto
            {
                Id = repliesReader.GetInt32(0),
                TicketId = repliesReader.GetInt32(1),
                SenderId = repliesReader.GetInt32(2),
                SenderName = repliesReader.GetString(3),
                IsAdminReply = repliesReader.GetInt32(4) == 1,
                Message = repliesReader.GetString(5),
                CreatedAt = repliesReader.GetDateTime(6)
            });
        }

        return Ok(new { ticket, replies });
    }

    /// <summary>
    /// Admin replies to a ticket
    /// </summary>
    public sealed class AdminReplyRequest
    {
        public string Message { get; set; } = "";
        public bool SendNotification { get; set; } = true;
    }

    [HttpPost("ticket/{ticketId}/reply")]
    public async Task<IActionResult> AdminReply(int ticketId, [FromBody] AdminReplyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest("Message is required");

        var adminId = GetUserId();
        if (adminId <= 0) adminId = 1; // Default for testing

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Get ticket owner
        await using var ownerCmd = conn.CreateCommand();
        ownerCmd.CommandText = "SELECT user_id, subject FROM support_tickets WHERE id = @ticketId";
        var pTid = ownerCmd.CreateParameter(); pTid.ParameterName = "@ticketId"; pTid.Value = ticketId; ownerCmd.Parameters.Add(pTid);

        int ownerId = 0;
        string subject = "";
        await using var ownerReader = await ownerCmd.ExecuteReaderAsync(ct);
        if (await ownerReader.ReadAsync(ct))
        {
            ownerId = ownerReader.GetInt32(0);
            subject = ownerReader.GetString(1);
        }
        await ownerReader.CloseAsync();

        if (ownerId == 0) return NotFound("Ticket not found");

        // Insert reply
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO support_ticket_replies (ticket_id, sender_id, is_admin_reply, message, created_at)
VALUES (@ticketId, @senderId, 1, @message, UTC_TIMESTAMP());

UPDATE support_tickets SET updated_at = UTC_TIMESTAMP(), status = 'in_progress' WHERE id = @ticketId AND status = 'open';
";
        var pTid2 = cmd.CreateParameter(); pTid2.ParameterName = "@ticketId"; pTid2.Value = ticketId; cmd.Parameters.Add(pTid2);
        var pSid = cmd.CreateParameter(); pSid.ParameterName = "@senderId"; pSid.Value = adminId; cmd.Parameters.Add(pSid);
        var pMsg = cmd.CreateParameter(); pMsg.ParameterName = "@message"; pMsg.Value = req.Message.Trim(); cmd.Parameters.Add(pMsg);

        await cmd.ExecuteNonQueryAsync(ct);

        // Send notification to user
        if (req.SendNotification)
        {
            await _notifs.NotifyComplaintResponseAsync(ownerId, ticketId, subject, ct);
        }

        await _audit.LogAsync(adminId, "support.reply", new { ticketId, ownerId }, ct);

        return Ok(new { message = "Reply sent" });
    }

    /// <summary>
    /// Update ticket status
    /// </summary>
    public sealed class UpdateStatusRequest
    {
        public string Status { get; set; } = "";
        public string? Priority { get; set; }
    }

    [HttpPost("ticket/{ticketId}/update")]
    public async Task<IActionResult> UpdateTicket(int ticketId, [FromBody] UpdateStatusRequest req, CancellationToken ct)
    {
        var validStatuses = new[] { "open", "in_progress", "resolved", "closed" };
        var validPriorities = new[] { "low", "normal", "high", "urgent" };

        if (!string.IsNullOrWhiteSpace(req.Status) && !validStatuses.Contains(req.Status.ToLower()))
            return BadRequest("Invalid status");

        if (!string.IsNullOrWhiteSpace(req.Priority) && !validPriorities.Contains(req.Priority.ToLower()))
            return BadRequest("Invalid priority");

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Get ticket owner for notification
        await using var ownerCmd = conn.CreateCommand();
        ownerCmd.CommandText = "SELECT user_id, subject FROM support_tickets WHERE id = @ticketId";
        var pTid = ownerCmd.CreateParameter(); pTid.ParameterName = "@ticketId"; pTid.Value = ticketId; ownerCmd.Parameters.Add(pTid);

        int ownerId = 0;
        string subject = "";
        await using var ownerReader = await ownerCmd.ExecuteReaderAsync(ct);
        if (await ownerReader.ReadAsync(ct))
        {
            ownerId = ownerReader.GetInt32(0);
            subject = ownerReader.GetString(1);
        }
        await ownerReader.CloseAsync();

        if (ownerId == 0) return NotFound("Ticket not found");

        // Build UPDATE
        var sets = new List<string> { "updated_at = UTC_TIMESTAMP()" };
        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            sets.Add("status = @status");
            if (req.Status.ToLower() == "resolved" || req.Status.ToLower() == "closed")
                sets.Add("resolved_at = UTC_TIMESTAMP()");
        }
        if (!string.IsNullOrWhiteSpace(req.Priority))
            sets.Add("priority = @priority");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE support_tickets SET {string.Join(", ", sets)} WHERE id = @ticketId";
        var pTid2 = cmd.CreateParameter(); pTid2.ParameterName = "@ticketId"; pTid2.Value = ticketId; cmd.Parameters.Add(pTid2);
        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            var p = cmd.CreateParameter(); p.ParameterName = "@status"; p.Value = req.Status.ToLower(); cmd.Parameters.Add(p);
        }
        if (!string.IsNullOrWhiteSpace(req.Priority))
        {
            var p = cmd.CreateParameter(); p.ParameterName = "@priority"; p.Value = req.Priority.ToLower(); cmd.Parameters.Add(p);
        }

        await cmd.ExecuteNonQueryAsync(ct);

        // Notify user of status change
        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            await _notifs.NotifyTicketStatusChangeAsync(ownerId, ticketId, subject, req.Status.ToLower(), ct);
        }

        var adminId = GetUserId();
        await _audit.LogAsync(adminId, "support.update", new { ticketId, req.Status, req.Priority }, ct);

        return Ok(new { message = "Ticket updated" });
    }

    /// <summary>
    /// Get ticket statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 
    COUNT(*) as total,
    SUM(CASE WHEN status = 'open' THEN 1 ELSE 0 END) as open_count,
    SUM(CASE WHEN status = 'in_progress' THEN 1 ELSE 0 END) as in_progress_count,
    SUM(CASE WHEN status = 'resolved' THEN 1 ELSE 0 END) as resolved_count,
    SUM(CASE WHEN status = 'closed' THEN 1 ELSE 0 END) as closed_count,
    SUM(CASE WHEN priority = 'urgent' THEN 1 ELSE 0 END) as urgent_count,
    SUM(CASE WHEN priority = 'high' THEN 1 ELSE 0 END) as high_count
FROM support_tickets;
";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return Ok(new
            {
                total = reader.GetInt64(0),
                open = reader.GetInt64(1),
                inProgress = reader.GetInt64(2),
                resolved = reader.GetInt64(3),
                closed = reader.GetInt64(4),
                urgent = reader.GetInt64(5),
                high = reader.GetInt64(6)
            });
        }

        return Ok(new { total = 0, open = 0, inProgress = 0, resolved = 0, closed = 0, urgent = 0, high = 0 });
    }

    private int GetUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
