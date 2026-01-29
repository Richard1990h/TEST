using System.Data;
using LittleHelperAI.Backend.Services.Admin;
using LittleHelperAI.Backend.Services.Notifications;
using LittleHelperAI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/notifications")]
public sealed class AdminNotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly NotificationStore _store;
    private readonly AdminAuditStore _audit;

    public AdminNotificationsController(
        ApplicationDbContext db,
        NotificationStore store,
        AdminAuditStore audit)
    {
        _db = db;
        _store = store;
        _audit = audit;
    }

    public sealed class SendRequest
    {
        public int? UserId { get; set; }
        public List<int>? UserIds { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? ActionUrl { get; set; }

        public bool AllUsers { get; set; }
        public string? Role { get; set; }
        public int? PlanId { get; set; }
    }

    // =========================
    // SEND NOTIFICATION
    // =========================
    [HttpPost("send")]
    public async Task<IActionResult> Send(
        [FromBody] SendRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title) ||
            string.IsNullOrWhiteSpace(req.Message))
            return BadRequest("Title and Message are required");

        var targets = await ResolveTargetsAsync(req, ct);

        if (targets.Count == 0)
            return BadRequest("No target users resolved");

        await _store.CreateManyAsync(
            targets,
            req.Title.Trim(),
            req.Message.Trim(),
            req.ActionUrl,
            ct
        );

        await _audit.WriteAsync(
            GetUserId(),
            "notify.send",
            "user_notifications",
            null,
            $"Sent '{req.Title}' to {targets.Count} user(s)",
            ct
        );

        return Ok(new { sent = targets.Count });
    }

    // =========================
    // TARGET RESOLUTION
    // =========================
    private async Task<List<int>> ResolveTargetsAsync(
        SendRequest req,
        CancellationToken ct)
    {
        // 1️⃣ Explicit user targets always win
        if (req.UserId is > 0)
            return new List<int> { req.UserId.Value };

        if (req.UserIds is { Count: > 0 })
            return req.UserIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        var sql = "SELECT DISTINCT u.id FROM users u ";
        var where = new List<string>();

        // 2️⃣ Plan targeting (active subscriptions ONLY)
        if (req.PlanId is > 0)
        {
            sql += @"
JOIN user_stripe_subscriptions s ON s.user_id = u.id
";
            where.Add("s.status = 'active'");
            where.Add("s.plan_id = @planId");
        }

        // 3️⃣ Role filter (optional)
        if (!string.IsNullOrWhiteSpace(req.Role))
            where.Add("u.role = @role");

        // 4️⃣ AllUsers only allowed when no other selector is present
        if (!req.AllUsers && where.Count == 0)
            return new List<int>();

        if (where.Count > 0)
            sql += " WHERE " + string.Join(" AND ", where);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (req.PlanId is > 0)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@planId";
            p.Value = req.PlanId.Value;
            cmd.Parameters.Add(p);
        }

        if (!string.IsNullOrWhiteSpace(req.Role))
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@role";
            p.Value = req.Role.Trim().ToLowerInvariant();
            cmd.Parameters.Add(p);
        }

        var list = new List<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(r.GetInt32(0));

        return list.Distinct().ToList();
    }

    // =========================
    // HELPERS
    // =========================
    private int GetUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
