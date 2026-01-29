using LittleHelperAI.Data;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Services;

public sealed class SubscriptionAccessService
{
    private readonly ApplicationDbContext _db;
    public SubscriptionAccessService(ApplicationDbContext db) => _db = db;

    public async Task<bool> HasActiveSubscriptionAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0) return false;

        var now = DateTime.UtcNow;

        return await _db.UserStripeSubscriptions
            .AsNoTracking()
            .AnyAsync(s => s.UserId == userId
                        && (s.Status == "active" || s.Status == "trialing")
                        && s.CurrentPeriodEndUtc > now,
                ct);
    }
}
