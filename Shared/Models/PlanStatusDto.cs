using System;

namespace LittleHelperAI.Shared.Models;

public sealed class PlanStatusDto
{
    public bool HasActivePlan { get; set; }
    public string? PlanName { get; set; } // optional friendly label
    public string? PlanType { get; set; } // e.g. Subscription / OneTime
    public bool IsUnlimited { get; set; }

    public double? DailyCreditsAllowance { get; set; }
    public double? DailyCreditsRemaining { get; set; }
    public DateTime? DailyCreditsNextResetUtc { get; set; }

    public double WalletCredits { get; set; } // existing user.Credits snapshot
}
