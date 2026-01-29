using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Backend.Services.Notifications;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Route("api/admin/rewards")]
public sealed class AdminRewardsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AdminAuditLogger _audit;
    private readonly NotificationService _notifs;

    public AdminRewardsController(
        ApplicationDbContext db,
        AdminAuditLogger audit,
        NotificationService notifs)
    {
        _db = db;
        _audit = audit;
        _notifs = notifs;
    }

    // ─────────────────────────────────────────────────────────────
    // GET REFERRAL SETTINGS
    // ─────────────────────────────────────────────────────────────
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var settings = await _db.ReferralSettings.FirstOrDefaultAsync(ct);

        if (settings == null)
        {
            settings = new ReferralSettings
            {
                Id = 1,
                ReferrerCredits = 50.0,
                RefereeCredits = 25.0,
                IsEnabled = true,
                UpdatedAt = DateTime.UtcNow
            };

            _db.ReferralSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(settings);
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATE REFERRAL SETTINGS
    // ─────────────────────────────────────────────────────────────
    [HttpPost("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateReferralSettingsRequest request,
        CancellationToken ct)
    {
        var settings = await _db.ReferralSettings.FirstOrDefaultAsync(ct);

        if (settings == null)
        {
            settings = new ReferralSettings { Id = 1 };
            _db.ReferralSettings.Add(settings);
        }

        settings.ReferrerCredits = request.ReferrerCredits;
        settings.RefereeCredits = request.RefereeCredits;
        settings.IsEnabled = request.IsEnabled;
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            GetActorUserId(),
            "rewards.settings.update",
            new
            {
                request.ReferrerCredits,
                request.RefereeCredits,
                request.IsEnabled
            },
            ct
        );

        return Ok(new { message = "Settings updated successfully", settings });
    }

    // ─────────────────────────────────────────────────────────────
    // GET REFERRAL STATISTICS (OPTIMIZED)
    // ─────────────────────────────────────────────────────────────
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var transactions = await _db.ReferralTransactions
            .AsNoTracking()
            .ToListAsync(ct);

        var users = await _db.Users
            .AsNoTracking()
            .ToDictionaryAsync(u => u.Id, ct);

        var totalReferrals = transactions.Count;
        var totalCreditsAwarded =
            transactions.Sum(t => t.ReferrerCreditsAwarded + t.RefereeCreditsAwarded);

        var activeReferrers =
            transactions.Select(t => t.ReferrerId).Distinct().Count();

        var topReferrers = transactions
            .GroupBy(t => t.ReferrerId)
            .Select(g => new TopReferrerDto
            {
                UserId = g.Key,
                Username = users.TryGetValue(g.Key, out var u) ? u.Username : "Unknown",
                Email = users.TryGetValue(g.Key, out var u2) ? u2.Email : "",
                ReferralCount = g.Count(),
                TotalCreditsEarned = g.Sum(x => x.ReferrerCreditsAwarded)
            })
            .OrderByDescending(x => x.ReferralCount)
            .Take(10)
            .ToList();

        var recentReferrals = transactions
            .OrderByDescending(t => t.CreatedAt)
            .Take(20)
            .Select(t => new RecentReferralDto
            {
                Id = t.Id,
                ReferrerUsername = users.TryGetValue(t.ReferrerId, out var r) ? r.Username : "Unknown",
                RefereeUsername = users.TryGetValue(t.RefereeId, out var f) ? f.Username : "Unknown",
                ReferralCode = t.ReferralCode,
                ReferrerCredits = t.ReferrerCreditsAwarded,
                RefereeCredits = t.RefereeCreditsAwarded,
                CreatedAt = t.CreatedAt
            })
            .ToList();

        return Ok(new ReferralStatsDto
        {
            TotalReferrals = totalReferrals,
            TotalCreditsAwarded = totalCreditsAwarded,
            ActiveReferrers = activeReferrers,
            TopReferrers = topReferrers,
            RecentReferrals = recentReferrals
        });
    }

    // ─────────────────────────────────────────────────────────────
    // GET ALL REFERRAL TRANSACTIONS
    // ─────────────────────────────────────────────────────────────
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(CancellationToken ct)
    {
        var users = await _db.Users
            .AsNoTracking()
            .ToDictionaryAsync(u => u.Id, ct);

        var transactions = await _db.ReferralTransactions
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var result = transactions.Select(t => new RecentReferralDto
        {
            Id = t.Id,
            ReferrerUsername = users.TryGetValue(t.ReferrerId, out var r) ? r.Username : "Unknown",
            RefereeUsername = users.TryGetValue(t.RefereeId, out var f) ? f.Username : "Unknown",
            ReferralCode = t.ReferralCode,
            ReferrerCredits = t.ReferrerCreditsAwarded,
            RefereeCredits = t.RefereeCreditsAwarded,
            CreatedAt = t.CreatedAt
        }).ToList();

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────
    // MANUAL AWARD (ADMIN OVERRIDE – AUDITED)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("manual-award")]
    public async Task<IActionResult> ManualAward(
        [FromBody] ManualAwardRequest request,
        CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user == null)
            return NotFound("User not found");

        user.Credits += request.Credits;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            GetActorUserId(),
            "rewards.manual_award",
            new
            {
                request.UserId,
                request.Credits,
                NewBalance = user.Credits
            },
            ct
        );

        await _notifs.SendAsync(
            user.Id,
            "🎁 Credits Awarded",
            $"An administrator awarded you {request.Credits} credits.",
            "/profile",
            ct
        );

        return Ok(new
        {
            message = $"Awarded {request.Credits} credits to {user.Username}",
            newBalance = user.Credits
        });
    }




    public sealed class ManualAwardRequest
    {
        public int UserId { get; set; }
        public double Credits { get; set; }
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────
    private int GetActorUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
