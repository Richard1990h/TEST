using LittleHelperAI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/users")]
public sealed class AdminUsersLookupController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public AdminUsersLookupController(ApplicationDbContext db) => _db = db;

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int take = 25,
        CancellationToken ct = default)
    {
        q = (q ?? "").Trim();
        if (q.Length < 1)
            return Ok(Array.Empty<object>());

        take = Math.Clamp(take, 1, 50);

        var baseQuery = _db.Users.AsNoTracking();

        if (int.TryParse(q, out var id))
        {
            baseQuery = baseQuery.Where(u => u.Id == id);
        }
        else
        {
            baseQuery = baseQuery.Where(u =>
                u.Username.Contains(q) ||
                u.Email.Contains(q) ||
                (u.FirstName != null && u.FirstName.Contains(q)) ||
                (u.LastName != null && u.LastName.Contains(q)));
        }

        var items = await baseQuery
            .OrderBy(u => u.Username)
            .Take(take)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.FirstName,
                u.LastName,

                // 🔽 Active subscription (if any)
                ActiveSubscription = _db.UserStripeSubscriptions
                    .Where(s =>
                        s.UserId == u.Id &&
                        s.Status == "active" &&
                        s.CurrentPeriodEndUtc > DateTime.UtcNow)
                    .OrderByDescending(s => s.UpdatedAt)
                    .Select(s => new
                    {
                        s.PlanId,
                        s.Status,
                        s.CurrentPeriodEndUtc,

                        // 🔽 Resolve tier via policy
                        PlanTier = _db.StripePlanPolicies
                            .Where(p => p.PlanId == s.PlanId)
                            .Select(p => p.PlanTier)
                            .FirstOrDefault()
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        // FREE fallback (no subscription)
        var result = items.Select(u => new
        {
            u.Id,
            u.Username,
            u.Email,
            u.FirstName,
            u.LastName,
            PlanTier = u.ActiveSubscription?.PlanTier ?? "FREE",
            HasActiveSubscription = u.ActiveSubscription != null,
            CurrentPeriodEndUtc = u.ActiveSubscription?.CurrentPeriodEndUtc
        });

        return Ok(result);
    }
}
