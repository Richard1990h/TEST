using LittleHelperAI.Backend.Services;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Authorize]
[Route("api/admin/plans")]
public sealed class AdminPlansController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AdminAuditLogger _audit;

    public AdminPlansController(ApplicationDbContext db, AdminAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    // =========================
    // LIST
    // =========================
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var plans = await _db.StripePlans.AsNoTracking().ToListAsync(ct);
        var policies = await _db.StripePlanPolicies.AsNoTracking().ToListAsync(ct);
        return Ok(new { plans, policies });
    }

    // =========================
    // UPSERT PLAN (billing only)
    // =========================
    [HttpPost("upsert-plan")]
    public async Task<IActionResult> UpsertPlan(
        [FromBody] StripePlan plan,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plan.PlanType))
            return BadRequest("PlanType is required.");

        if (plan.Id == 0)
        {
            _db.StripePlans.Add(plan);
        }
        else
        {
            var existing = await _db.StripePlans
                .FirstOrDefaultAsync(p => p.Id == plan.Id, ct);

            if (existing == null)
                return NotFound();

            existing.Credits = plan.Credits;
            existing.PriceId = plan.PriceId;
            existing.PlanType = plan.PlanType;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            GetActorUserId(),
            "plan.upsert",
            new
            {
                plan.Id,
                plan.PlanType,
                plan.Credits
            },
            ct
        );

        return Ok(plan);
    }

    // =========================
    // UPSERT POLICY (behaviour)
    // =========================
    [HttpPost("upsert-policy")]
    public async Task<IActionResult> UpsertPolicy(
        [FromBody] StripePlanPolicy policy,
        CancellationToken ct)
    {
        // FREE tier uses PlanId = 0
        if (policy.PlanId < 0)
            return BadRequest("Invalid PlanId.");

        if (policy.PlanId > 0)
        {
            var exists = await _db.StripePlans
                .AnyAsync(p => p.Id == policy.PlanId, ct);

            if (!exists)
                return BadRequest("Plan does not exist.");
        }

        // Tier enforcement
        if (policy.PlanTier == "UNLIMITED")
        {
            policy.IsUnlimited = true;
            policy.DailyCredits = null;
        }

        if (policy.PlanTier == "FREE")
        {
            policy.IsUnlimited = false;
        }

        var existing = await _db.StripePlanPolicies
            .FirstOrDefaultAsync(p => p.PlanId == policy.PlanId, ct);

        if (existing == null)
        {
            _db.StripePlanPolicies.Add(policy);
        }
        else
        {
            existing.PlanName = policy.PlanName;
            existing.PlanTier = policy.PlanTier;
            existing.IsUnlimited = policy.IsUnlimited;
            existing.DailyCredits = policy.DailyCredits;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            GetActorUserId(),
            "policy.upsert",
            new
            {
                policy.PlanId,
                policy.PlanTier,
                policy.IsUnlimited,
                policy.DailyCredits
            },
            ct
        );

        return Ok(policy);
    }

    // =========================
    // DELETE PLAN
    // =========================
    [HttpDelete("{planId:int}")]
    public async Task<IActionResult> DeletePlan(int planId, CancellationToken ct)
    {
        if (planId == 0)
            return BadRequest("FREE plan cannot be deleted.");

        var plan = await _db.StripePlans
            .FirstOrDefaultAsync(p => p.Id == planId, ct);

        if (plan == null)
            return NotFound();

        var policy = await _db.StripePlanPolicies
            .FirstOrDefaultAsync(p => p.PlanId == planId, ct);

        if (policy != null)
            _db.StripePlanPolicies.Remove(policy);

        _db.StripePlans.Remove(plan);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            GetActorUserId(),
            "plan.delete",
            new { planId },
            ct
        );

        return Ok(new { deleted = true });
    }

    private int GetActorUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
