using System.Data;
using LittleHelperAI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Route("api/admin/stats")]
public sealed class AdminStatsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public AdminStatsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        async Task<long> CountAsync(string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var o = await cmd.ExecuteScalarAsync(ct);
            return o is null || o == DBNull.Value ? 0 : Convert.ToInt64(o);
        }

        // =========================
        // EXISTING STATS (UNCHANGED)
        // =========================
        var users = await CountAsync("SELECT COUNT(*) FROM users;");
        var chats = await CountAsync("SELECT COUNT(*) FROM chathistory;");
        var knowledge = await CountAsync("SELECT COUNT(*) FROM knowledge_entries;");
        var notifications = await CountAsync("SELECT COUNT(*) FROM user_notifications;");
        var unreadNotifications = await CountAsync("SELECT COUNT(*) FROM user_notifications WHERE is_read = 0;");
        var audit = await CountAsync("SELECT COUNT(*) FROM admin_audit_log;");

        var llmCalls24h = await CountAsync(
            "SELECT COUNT(*) FROM llm_calls WHERE created_at >= (UTC_TIMESTAMP() - INTERVAL 24 HOUR);"
        );

        var buildRepairs = await CountAsync("SELECT COUNT(*) FROM build_repairs;");
        var activeSubs = await CountAsync(
            "SELECT COUNT(*) FROM user_stripe_subscriptions WHERE status = 'active';"
        );

        // =========================
        // NEW: TIER STATS (SAFE)
        // =========================

        // FREE users = no active subscription
        var freeUsers = await CountAsync(@"
SELECT COUNT(*) 
FROM users u
WHERE NOT EXISTS (
    SELECT 1
    FROM user_stripe_subscriptions s
    WHERE s.user_id = u.id
      AND s.status = 'active'
      AND (s.current_period_end_utc IS NULL OR s.current_period_end_utc > UTC_TIMESTAMP())
);");

        // Paid users = any active subscription
        var paidUsers = await CountAsync(@"
SELECT COUNT(DISTINCT s.user_id)
FROM user_stripe_subscriptions s
WHERE s.status = 'active'
  AND (s.current_period_end_utc IS NULL OR s.current_period_end_utc > UTC_TIMESTAMP());
");

        // Unlimited users = active + unlimited policy
        var unlimitedUsers = await CountAsync(@"
SELECT COUNT(DISTINCT s.user_id)
FROM user_stripe_subscriptions s
JOIN stripeplan_policies p ON p.plan_id = s.plan_id
WHERE s.status = 'active'
  AND p.is_unlimited = 1
  AND (s.current_period_end_utc IS NULL OR s.current_period_end_utc > UTC_TIMESTAMP());
");

        return Ok(new
        {
            // existing
            users,
            chats,
            knowledge,
            notifications,
            unreadNotifications,
            audit,
            llmCalls24h,
            buildRepairs,
            activeSubs,

            // new tier stats
            freeUsers,
            paidUsers,
            unlimitedUsers
        });
    }
}
