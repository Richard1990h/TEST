using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Backend.Services.Notifications;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Controllers;

/// <summary>
/// User-facing subscription management endpoints
/// </summary>
[ApiController]
[Route("api/subscriptions")]
[Authorize]
public sealed class UserSubscriptionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notifs;
    private readonly ILogger<UserSubscriptionsController> _logger;

    public UserSubscriptionsController(
        ApplicationDbContext db,
        NotificationService notifs,
        ILogger<UserSubscriptionsController> logger)
    {
        _db = db;
        _notifs = notifs;
        _logger = logger;
    }

    // =========================
    // GET MY SUBSCRIPTIONS
    // =========================
    [HttpGet("my")]
    public async Task<IActionResult> GetMySubscriptions(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        var subscriptions = await _db.UserStripeSubscriptions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                PlanId = s.PlanId,
                Status = s.Status,
                CurrentPeriodEndUtc = s.CurrentPeriodEndUtc,
                CreatedAt = s.CreatedAt,
                IsCanceled = s.Status == "canceled" || s.Status == "cancel_at_period_end",
                WillCancelAtPeriodEnd = s.Status == "cancel_at_period_end"
            })
            .ToListAsync(ct);

        // Get plan names
        var planIds = subscriptions.Select(s => s.PlanId).Distinct().ToList();
        var plans = await _db.StripePlans
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => $"{p.Credits} Credits ({p.PlanType})", ct);

        foreach (var sub in subscriptions)
        {
            sub.PlanName = plans.GetValueOrDefault(sub.PlanId, $"Plan #{sub.PlanId}");
        }

        return Ok(subscriptions);
    }

    // =========================
    // GET ACTIVE SUBSCRIPTION
    // =========================
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveSubscription(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        var activeSub = await _db.UserStripeSubscriptions
            .Where(s => s.UserId == userId && 
                       (s.Status == "active" || s.Status == "trialing" || s.Status == "cancel_at_period_end") &&
                       s.CurrentPeriodEndUtc > DateTime.UtcNow)
            .OrderByDescending(s => s.CurrentPeriodEndUtc)
            .FirstOrDefaultAsync(ct);

        if (activeSub == null)
            return Ok(new { hasActiveSubscription = false });

        var plan = await _db.StripePlans.FindAsync(new object[] { activeSub.PlanId }, ct);

        return Ok(new
        {
            hasActiveSubscription = true,
            subscription = new UserSubscriptionDto
            {
                Id = activeSub.Id,
                PlanId = activeSub.PlanId,
                PlanName = plan != null ? $"{plan.Credits} Credits ({plan.PlanType})" : $"Plan #{activeSub.PlanId}",
                Status = activeSub.Status,
                CurrentPeriodEndUtc = activeSub.CurrentPeriodEndUtc,
                CreatedAt = activeSub.CreatedAt,
                IsCanceled = activeSub.Status == "canceled" || activeSub.Status == "cancel_at_period_end",
                WillCancelAtPeriodEnd = activeSub.Status == "cancel_at_period_end"
            }
        });
    }

    // =========================
    // CANCEL MY SUBSCRIPTION
    // =========================
    public sealed class CancelMySubscriptionRequest
    {
        public int SubscriptionId { get; set; }
        public bool Immediate { get; set; } = false; // If false, cancel at period end
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> CancelMySubscription(
        [FromBody] CancelMySubscriptionRequest req,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Unauthorized();

        var sub = await _db.UserStripeSubscriptions
            .FirstOrDefaultAsync(s => s.Id == req.SubscriptionId && s.UserId == userId, ct);

        if (sub == null)
            return NotFound("Subscription not found.");

        if (sub.Status == "canceled")
            return BadRequest("This subscription is already canceled.");

        var previousStatus = sub.Status;

        if (req.Immediate)
        {
            // Immediate cancellation
            sub.Status = "canceled";
            sub.CurrentPeriodEndUtc = DateTime.UtcNow;
        }
        else
        {
            // Cancel at end of billing period - user keeps access until then
            sub.Status = "cancel_at_period_end";
        }

        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // 🔔 Notification
        var message = req.Immediate
            ? "Your subscription has been canceled. You are now on the Free plan."
            : $"Your subscription will be canceled at the end of your billing period ({sub.CurrentPeriodEndUtc:MMM dd, yyyy}). You will keep access until then.";

        await _notifs.SendAsync(
            userId,
            "📋 Subscription Cancellation",
            message,
            "/plans",
            ct
        );

        _logger.LogInformation(
            "User {UserId} canceled subscription {SubId}. Previous: {Previous}, New: {New}, Immediate: {Immediate}",
            userId, sub.Id, previousStatus, sub.Status, req.Immediate);

        return Ok(new
        {
            message = req.Immediate 
                ? "Subscription canceled immediately." 
                : $"Subscription will be canceled on {sub.CurrentPeriodEndUtc:MMM dd, yyyy}.",
            subscription = new UserSubscriptionDto
            {
                Id = sub.Id,
                PlanId = sub.PlanId,
                Status = sub.Status,
                CurrentPeriodEndUtc = sub.CurrentPeriodEndUtc,
                IsCanceled = true,
                WillCancelAtPeriodEnd = sub.Status == "cancel_at_period_end"
            }
        });
    }

    // =========================
    // HELPERS
    // =========================
    private int GetCurrentUserId()
    {
        var idClaim = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(idClaim, out var id) ? id : 0;
    }
}

/// <summary>
/// DTO for user-facing subscription info
/// </summary>
public sealed class UserSubscriptionDto
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public string PlanName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CurrentPeriodEndUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCanceled { get; set; }
    public bool WillCancelAtPeriodEnd { get; set; }
}
