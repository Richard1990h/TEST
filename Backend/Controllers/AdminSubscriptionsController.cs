using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Backend.Services.Notifications;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/subscriptions")]
public sealed class AdminSubscriptionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AdminAuditLogger _audit;
    private readonly NotificationService _notifs;

    public AdminSubscriptionsController(
        ApplicationDbContext db,
        AdminAuditLogger audit,
        NotificationService notifs)
    {
        _db = db;
        _audit = audit;
        _notifs = notifs;
    }

    // =========================
    // LIST (ADMIN VIEW MODEL)
    // =========================
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int take = 200,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(skip, 0);

        var total = await _db.UserStripeSubscriptions
            .AsNoTracking()
            .CountAsync(ct);

        var items = await _db.UserStripeSubscriptions
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .Join(
                _db.Users.AsNoTracking(),
                s => s.UserId,
                u => u.Id,
                (s, u) => new { s, u }
            )
            // LEFT JOIN policies (FREE fallback safe)
            .GroupJoin(
                _db.StripePlanPolicies.AsNoTracking(),
                su => su.s.PlanId,
                p => p.PlanId,
                (su, pg) => new { su, p = pg.FirstOrDefault() }
            )
            .Select(x => new AdminSubscriptionRow
            {
                Id = x.su.s.Id,
                UserId = x.su.s.UserId,
                UserName =
                    (!string.IsNullOrWhiteSpace(x.su.u.FirstName) || !string.IsNullOrWhiteSpace(x.su.u.LastName))
                        ? $"{x.su.u.FirstName} {x.su.u.LastName}".Trim()
                        : x.su.u.Username,
                Email = x.su.u.Email,
                PlanId = x.su.s.PlanId,
                // ✅ Synchronized with CreditPolicyService logic
                PlanTier = (x.su.s.Status == "active" || x.su.s.Status == "trialing") && x.su.s.CurrentPeriodEndUtc > DateTime.UtcNow
                    ? (x.p == null ? "FREE" : x.p.PlanTier)
                    : "FREE",
                IsUnlimited = x.p != null && x.p.IsUnlimited,
                PriceId = x.su.s.PriceId,
                SubscriptionId = x.su.s.SubscriptionId,
                Status = x.su.s.Status,
                CurrentPeriodEndUtc = x.su.s.CurrentPeriodEndUtc,
                CreatedAt = x.su.s.CreatedAt,
                UpdatedAt = x.su.s.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(new { total, items });
    }

    // =========================
    // UPDATE
    // =========================
    public sealed class UpdateSubscriptionRequest
    {
        public int Id { get; set; }
        public string? Status { get; set; }
        public DateTime? CurrentPeriodEndUtc { get; set; }

        // ✅ ADD ONLY — required to edit plan
        public int? PlanId { get; set; }
    }


    [HttpPost("update")]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSubscriptionRequest req,
        CancellationToken ct)
    {
        if (req.Id <= 0)
            return BadRequest("Id is required.");

        var sub = await _db.UserStripeSubscriptions
            .FirstOrDefaultAsync(s => s.Id == req.Id, ct);

        if (sub == null)
            return NotFound();

        // Capture previous state (for notifications / audit)
        var oldPlanId = sub.PlanId;
        var oldStatus = sub.Status;

        // 🔹 STATUS
        if (!string.IsNullOrWhiteSpace(req.Status))
            sub.Status = req.Status.Trim().ToLowerInvariant();

        // 🔹 PERIOD END
        if (req.CurrentPeriodEndUtc.HasValue)
            sub.CurrentPeriodEndUtc =
                DateTime.SpecifyKind(req.CurrentPeriodEndUtc.Value, DateTimeKind.Utc);

        // 🔹 PLAN (ADMIN EDIT — THIS WAS MISSING)
        if (req.PlanId.HasValue &&
            req.PlanId.Value > 0 &&
            req.PlanId.Value != sub.PlanId)
        {
            sub.PlanId = req.PlanId.Value;
        }

        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // 🔔 USER NOTIFICATION (no Plans table required)
        if (oldPlanId != sub.PlanId)
        {
            await _notifs.SendAsync(
                sub.UserId,
                "📋 Subscription Updated",
                "Your subscription plan has been updated by an administrator.",
                "/plans",
                ct
            );
        }
        else if (oldStatus != sub.Status)
        {
            await _notifs.SendAsync(
                sub.UserId,
                "📋 Subscription Updated",
                $"Your subscription status is now '{sub.Status}'.",
                "/plans",
                ct
            );
        }

        // 🧾 AUDIT LOG (accurate, minimal)
        await _audit.LogAsync(
            GetActorUserId(),
            "subscription.update",
            new
            {
                sub.Id,
                sub.UserId,
                OldPlanId = oldPlanId,
                NewPlanId = sub.PlanId,
                OldStatus = oldStatus,
                NewStatus = sub.Status,
                sub.CurrentPeriodEndUtc
            },
            ct
        );

        return Ok(sub);
    }



    // =========================
    // CANCEL
    // =========================
    public sealed class CancelSubscriptionRequest
    {
        public int Id { get; set; }
        public bool Immediate { get; set; } = false;
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(
        [FromBody] CancelSubscriptionRequest req,
        CancellationToken ct)
    {
        if (req.Id <= 0)
            return BadRequest("Id is required.");

        var sub = await _db.UserStripeSubscriptions
            .FirstOrDefaultAsync(s => s.Id == req.Id, ct);

        if (sub == null)
            return NotFound("Subscription not found.");

        var previousStatus = sub.Status;

        if (req.Immediate)
        {
            sub.Status = "canceled";
            sub.CurrentPeriodEndUtc = DateTime.UtcNow;
        }
        else
        {
            sub.Status = "cancel_at_period_end";
        }

        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var message = req.Immediate
            ? "Your subscription has been canceled immediately by an administrator."
            : $"Your subscription will be canceled at the end of your billing period ({sub.CurrentPeriodEndUtc:MMM dd, yyyy}).";

        await _notifs.SendAsync(
            sub.UserId,
            "❌ Subscription Canceled",
            message,
            "/plans",
            ct
        );

        await _audit.LogAsync(
            GetActorUserId(),
            "subscription.cancel",
            new
            {
                sub.Id,
                sub.UserId,
                sub.PlanId,
                PreviousStatus = previousStatus,
                NewStatus = sub.Status,
                Immediate = req.Immediate
            },
            ct
        );

        return Ok(new { message = "Subscription canceled successfully.", subscription = sub });
    }



    // =========================
    // PLANS (ADMIN UI – FLAT LIST)
    // =========================
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken ct = default)
    {
        var plans = await _db.StripePlans
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.PriceId,
                p.PlanType,
                p.PlanTier,
                p.Credits,
                p.DailyCredits,
                p.IsUnlimited,
                p.Description
            })
            .ToListAsync(ct);

        // ⚠️ IMPORTANT:
        // Return ARRAY ONLY (not wrapped)
        return Ok(plans);
    }






    // =========================
    // ASSIGN
    // =========================
    public sealed class AssignSubscriptionRequest
    {
        public int UserId { get; set; }
        public int PlanId { get; set; }
        public string? PriceId { get; set; }
        public string Status { get; set; } = "active";
        public DateTime CurrentPeriodEndUtc { get; set; } = DateTime.UtcNow.AddDays(30);
        public string? SubscriptionId { get; set; }
    }






    [HttpPost("assign")]
    public async Task<IActionResult> Assign(
        [FromBody] AssignSubscriptionRequest req,
        CancellationToken ct)
    {
        if (req.UserId <= 0 || req.PlanId <= 0)
            return BadRequest("UserId and PlanId are required.");

        if (!await _db.Users.AnyAsync(u => u.Id == req.UserId, ct))
            return BadRequest("User does not exist.");

        // ❗ Prevent multiple active subscriptions
        var hasActive = await _db.UserStripeSubscriptions.AnyAsync(s =>
            s.UserId == req.UserId &&
            s.Status == "active" &&
            s.CurrentPeriodEndUtc > DateTime.UtcNow,
            ct);

        if (hasActive)
            return BadRequest("User already has an active subscription.");

        var plan = await _db.StripePlans
            .FirstOrDefaultAsync(p => p.Id == req.PlanId, ct);

        if (plan == null)
            return BadRequest("Plan does not exist.");

        var policy = await _db.StripePlanPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlanId == plan.Id, ct);

        if (policy == null)
            return BadRequest("Plan policy not configured.");

        var sub = new UserStripeSubscription
        {
            UserId = req.UserId,
            PlanId = req.PlanId,
            PriceId = string.IsNullOrWhiteSpace(req.PriceId) ? plan.PriceId : req.PriceId!,
            SubscriptionId = string.IsNullOrWhiteSpace(req.SubscriptionId)
                ? $"manual_{Guid.NewGuid():N}"
                : req.SubscriptionId!,
            Status = string.IsNullOrWhiteSpace(req.Status)
                ? "active"
                : req.Status.Trim().ToLowerInvariant(),
            CurrentPeriodEndUtc =
                DateTime.SpecifyKind(req.CurrentPeriodEndUtc, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.UserStripeSubscriptions.Add(sub);
        await _db.SaveChangesAsync(ct);

        await _notifs.NotifySubscriptionActivatedAsync(
            sub.UserId,
            $"{policy.PlanTier} Plan Activated",
            ct
        );

        await _audit.LogAsync(
            GetActorUserId(),
            "subscription.assign",
            new { sub.Id, sub.UserId, sub.PlanId, policy.PlanTier, sub.Status },
            ct
        );

        return Ok(sub);
    }

    // =========================
    // HELPERS
    // =========================
    private int GetActorUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }

    // =========================
    // ADMIN VIEW MODEL
    // =========================
    public sealed class AdminSubscriptionRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public int PlanId { get; set; }
        public string PlanTier { get; set; } = "";
        public bool IsUnlimited { get; set; }
        public string PriceId { get; set; } = "";
        public string SubscriptionId { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CurrentPeriodEndUtc { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
