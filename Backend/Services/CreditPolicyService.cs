using LittleHelperAI.Data;
using LittleHelperAI.Backend.Services.Notifications;
using LittleHelperAI.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// Central policy that decides whether a request is allowed (unlimited plan, daily credits, or wallet credits).
/// ADD-ONLY: does not remove existing credit accounting, but can prevent wallet deductions when plan/daily applies.
/// </summary>
public sealed class CreditPolicyService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<CreditPolicyService> _logger;
    private readonly NotificationStore _notifs;
public CreditPolicyService(ApplicationDbContext db, IConfiguration config, ILogger<CreditPolicyService> logger, NotificationStore notifs)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _notifs = notifs;
}

    public sealed class ConsumeResult
    {
        public bool Allowed { get; init; }
        public string? DenyReason { get; init; }

        public bool IsUnlimited { get; init; }

        public bool UsedDailyCredits { get; init; }
        public double? DailyAllowance { get; init; }
        public double? DailyRemaining { get; init; }
        public DateTime? DailyNextResetUtc { get; init; }

        public double WalletCreditsBefore { get; init; }
        public double WalletCreditsAfter { get; init; }

        public int? ActivePlanId { get; init; }
        public string? ActivePlanName { get; init; }
    }

    /// <summary>
    /// Attempts to consume "amount" credits for the given user.
    /// amount is in your existing credit units (TokenCounter.CalculateCreditCost output).
    /// </summary>
    public async Task<ConsumeResult> TryConsumeAsync(int userId, double amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return new ConsumeResult
            {
                Allowed = true,
                WalletCreditsBefore = 0,
                WalletCreditsAfter = 0
            };
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return new ConsumeResult { Allowed = false, DenyReason = "User not found" };
        }

        var walletBefore = user.Credits;

        // 1) Resolve active plan (subscription-backed if present, otherwise latest subscription purchase fallback)
        var (planId, planName, isUnlimited, dailyFromPlan) = await ResolvePlanAsync(userId, ct);

        if (isUnlimited)
        {
            // Unlimited: allow but do NOT subtract wallet; keep analytics elsewhere (chat history cost etc.)
            return new ConsumeResult
            {
                Allowed = true,
                IsUnlimited = true,
                ActivePlanId = planId,
                ActivePlanName = planName,
                WalletCreditsBefore = walletBefore,
                WalletCreditsAfter = walletBefore,
                DailyAllowance = dailyFromPlan,
                DailyRemaining = null,
                DailyNextResetUtc = ComputeNextResetUtc()
            };
        }

        // 2) Daily credits (free users and/or plans)
        var dailyAllowance = await ResolveDailyAllowanceAsync(userId, dailyFromPlan, ct);
        if (dailyAllowance > 0)
        {
            var daily = await GetOrResetDailyStateAsync(userId, dailyAllowance, ct);
            if (daily.DailyRemaining >= amount)
            {
                daily.DailyRemaining -= amount;
                daily.UpdatedAt = DateTime.UtcNow;

                // Wallet is the source of truth for total credits, so always deduct from wallet too.
                user.Credits = Math.Max(0, user.Credits - amount);

                await _db.SaveChangesAsync(ct);return new ConsumeResult
                {
                    Allowed = true,
                    UsedDailyCredits = true,
                    DailyAllowance = daily.DailyAllowance,
                    DailyRemaining = daily.DailyRemaining,
                    DailyNextResetUtc = ComputeNextResetUtc(),
                    ActivePlanId = planId,
                    ActivePlanName = planName,
                    WalletCreditsBefore = walletBefore,
                    WalletCreditsAfter = walletBefore
                };
            }
        }

        // 3) Wallet credits (existing behavior)
        if (walletBefore < amount)
        {
            return new ConsumeResult
            {
                Allowed = false,
                DenyReason = $"Insufficient credits. Need {amount:0.##}, have {walletBefore:0.##}.",
                ActivePlanId = planId,
                ActivePlanName = planName,
                WalletCreditsBefore = walletBefore,
                WalletCreditsAfter = walletBefore,
                DailyAllowance = dailyAllowance > 0 ? dailyAllowance : null,
                DailyRemaining = dailyAllowance > 0 ? (await GetOrResetDailyStateAsync(userId, dailyAllowance, ct)).DailyRemaining : null,
                DailyNextResetUtc = ComputeNextResetUtc()
            };
        }

        user.Credits = Math.Max(0, walletBefore - amount);
        await _db.SaveChangesAsync(ct);

        return new ConsumeResult
        {
            Allowed = true,
            ActivePlanId = planId,
            ActivePlanName = planName,
            WalletCreditsBefore = walletBefore,
            WalletCreditsAfter = user.Credits,
            DailyAllowance = dailyAllowance > 0 ? dailyAllowance : null,
            DailyRemaining = dailyAllowance > 0 ? (await GetOrResetDailyStateAsync(userId, dailyAllowance, ct)).DailyRemaining : null,
            DailyNextResetUtc = ComputeNextResetUtc()
        };
    }

    public async Task<PlanStatusDto> GetPlanStatusAsync(
        int userId,
        CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return new PlanStatusDto
            {
                HasActivePlan = true,
                PlanName = "FREE",
                PlanType = "Free",
                IsUnlimited = false,
                WalletCredits = 0
            };
        }

        // Existing resolver (DO NOT CHANGE)
        var (planId, planName, isUnlimited, dailyFromPlan)
            = await ResolvePlanAsync(userId, ct);

        // 🔧 FIX: only true if an actual plan exists
        var hasActivePlan =
            !string.IsNullOrWhiteSpace(planName) &&
            !string.Equals(planName, "FREE", StringComparison.OrdinalIgnoreCase);

        var displayPlan =
            string.IsNullOrWhiteSpace(planName)
                ? "FREE"
                : planName.ToUpperInvariant();

        var dailyAllowance =
            isUnlimited ? 0 : await ResolveDailyAllowanceAsync(userId, dailyFromPlan, ct);

        UserDailyCreditState? daily = null;
        if (dailyAllowance > 0)
        {
            daily = await GetOrResetDailyStateAsync(userId, dailyAllowance, ct);
        }

        return new PlanStatusDto
        {
            HasActivePlan = hasActivePlan,
            PlanName = displayPlan,
            PlanType = hasActivePlan ? "Subscription" : "Free",
            IsUnlimited = isUnlimited,
            DailyCreditsAllowance = dailyAllowance > 0 ? dailyAllowance : null,
            DailyCreditsRemaining = daily?.DailyRemaining,
            DailyCreditsNextResetUtc =
                dailyAllowance > 0 ? ComputeNextResetUtc() : null,
            WalletCredits = user.Credits
        };
    }


   

    private DateTime ComputeNextResetUtc()
    {
        // Configurable reset hour (UTC). Default midnight.
        var resetHour = _config.GetValue<int?>("Credits:DailyResetHourUtc") ?? 0;
        var now = DateTime.UtcNow;
        var todayReset = new DateTime(now.Year, now.Month, now.Day, resetHour, 0, 0, DateTimeKind.Utc);
        return now < todayReset ? todayReset : todayReset.AddDays(1);
    }

    private async Task<UserDailyCreditState> GetOrResetDailyStateAsync(int userId, double allowance, CancellationToken ct)
{
    var utcDay = DateTime.UtcNow.Date;

    var state = await _db.UserDailyCreditStates.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    var created = false;
    var reset = false;

    if (state is null)
    {
        state = new UserDailyCreditState
        {
            UserId = userId,
            UtcDay = utcDay,
            DailyAllowance = allowance,
            DailyRemaining = allowance,
            UpdatedAt = DateTime.UtcNow
        };
        _db.UserDailyCreditStates.Add(state);
        created = true;
    }
    else if (state.UtcDay != utcDay)
    {
        state.UtcDay = utcDay;
        state.DailyAllowance = allowance;
        state.DailyRemaining = allowance;
        state.UpdatedAt = DateTime.UtcNow;
        reset = true;
    }
    else
    {
        // Same UTC day: keep remaining, but allowance can change (e.g. plan changed)
        if (Math.Abs(state.DailyAllowance - allowance) > 0.0001)
        {
            var delta = allowance - state.DailyAllowance;
            state.DailyAllowance = allowance;

            // If allowance increased mid-day, increase remaining by the same delta (never below 0)
            if (delta > 0)
                state.DailyRemaining += delta;

            state.UpdatedAt = DateTime.UtcNow;
        }
    }

    // Grant daily credits into wallet (main credits) once per UTC day.
    // DailyRemaining remains a daily spending cap, but wallet is the source of truth for total credits.
    if ((created || reset) && allowance > 0)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            user.Credits += allowance;

            await _notifs.CreateAsync(
                userId,
                "Daily credits received",
                $"You received {allowance:0.##} daily credits.",
                "/profile/credits",
                ct);
        }
    }

    if (created || reset)
        await _db.SaveChangesAsync(ct);

    return state;
}

    private Task<double> ResolveDailyAllowanceAsync(int userId, double? planDaily, CancellationToken ct)
    {
        if (planDaily.HasValue && planDaily.Value > 0)
            return Task.FromResult(planDaily.Value);

        // Free-user daily credits default (can be 0 to disable)
        var freeDaily = _config.GetValue<double?>("Credits:FreeDailyCredits") ?? 0;
        return Task.FromResult(freeDaily);
    }

   
    private async Task<(int? planId, string planName, bool isUnlimited, double? dailyFromPlan)>
    ResolvePlanAsync(int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // ✅ ONLY source of truth: ACTIVE subscription
        var sub = await _db.UserStripeSubscriptions
            .Where(s =>
                s.UserId == userId &&
                (s.Status == "active" || s.Status == "trialing") &&
                s.CurrentPeriodEndUtc > now)
            .OrderByDescending(s => s.CurrentPeriodEndUtc)
            .FirstOrDefaultAsync(ct);

        // 🔴 NO ACTIVE SUBSCRIPTION = FREE
        if (sub == null)
            return (null, "FREE", false, null);

        // Try to get policy first, then fallback to StripePlan
        var policy = await _db.StripePlanPolicies
            .FirstOrDefaultAsync(p => p.PlanId == sub.PlanId, ct);

        var plan = await _db.StripePlans
            .FirstOrDefaultAsync(p => p.Id == sub.PlanId, ct);

        // Use PlanTier as the authoritative name for the UI
        var planName =
            policy?.PlanTier ??
            plan?.PlanTier ??
            policy?.PlanName ??
            plan?.Description ??
            "FREE";

        var isUnlimited = policy?.IsUnlimited ?? plan?.IsUnlimited ?? false;
        var dailyCredits = policy?.DailyCredits ?? (double?)plan?.DailyCredits;

        return (
            sub.PlanId,
            planName.ToUpperInvariant(),
            isUnlimited,
            dailyCredits
        );
    }


}
