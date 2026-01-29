using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LittleHelperAI.Data;
using LittleHelperAI.Backend.Services.Notifications;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// Background service that automatically downgrades expired subscriptions to free.
/// Runs every 5 minutes to check for subscriptions past their end date.
/// </summary>
public sealed class SubscriptionExpiryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryHostedService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    public SubscriptionExpiryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionExpiryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SubscriptionExpiryHostedService started. Checking every {Interval} minutes.", CheckInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndExpireSubscriptionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking subscription expiry");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndExpireSubscriptionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifs = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now = DateTime.UtcNow;

        // Find subscriptions that:
        // 1. Are marked "cancel_at_period_end" and have passed their end date
        // 2. Are "active" but have passed their end date (shouldn't happen normally, but safety check)
        var expiredSubscriptions = await db.UserStripeSubscriptions
            .Where(s => 
                (s.Status == "cancel_at_period_end" || s.Status == "active" || s.Status == "trialing") &&
                s.CurrentPeriodEndUtc <= now)
            .ToListAsync(ct);

        if (expiredSubscriptions.Count == 0)
            return;

        _logger.LogInformation("Found {Count} expired subscriptions to process.", expiredSubscriptions.Count);

        foreach (var sub in expiredSubscriptions)
        {
            var previousStatus = sub.Status;
            sub.Status = "canceled";
            sub.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Expiring subscription {SubId} for user {UserId}. Previous status: {Previous}, End date was: {EndDate}",
                sub.Id, sub.UserId, previousStatus, sub.CurrentPeriodEndUtc);

            // Send notification to user
            try
            {
                await notifs.SendAsync(
                    sub.UserId,
                    "📋 Subscription Expired",
                    "Your subscription has ended and you are now on the Free plan. Upgrade anytime to get more features!",
                    "/plans",
                    ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send expiry notification to user {UserId}", sub.UserId);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Processed {Count} expired subscriptions.", expiredSubscriptions.Count);
    }
}
