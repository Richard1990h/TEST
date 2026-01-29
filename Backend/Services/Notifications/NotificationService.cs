using LittleHelperAI.Data;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Services.Notifications;

/// <summary>
/// Centralized notification service for sending all types of notifications
/// - Daily credits reset
/// - Complaint responses
/// - New messages/emails
/// - Promotions
/// - Subscription updates
/// - System alerts
/// </summary>
public sealed class NotificationService
{
    private readonly NotificationStore _store;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        NotificationStore store,
        ApplicationDbContext db,
        ILogger<NotificationService> logger)
    {
        _store = store;
        _db = db;
        _logger = logger;
    }

    // ============================================
    // DAILY CREDITS NOTIFICATIONS
    // ============================================
    
    /// <summary>
    /// Notify user about daily credits reset
    /// </summary>
    public async Task NotifyDailyCreditsResetAsync(int userId, double creditsAmount, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "🎁 Daily Credits Reset",
            $"Your daily credits have been refreshed! You now have {creditsAmount:0.##} credits available.",
            "/plans",
            ct
        );
        _logger.LogInformation("Daily credits notification sent to user {UserId}", userId);
    }

    /// <summary>
    /// Notify user when credits are low
    /// </summary>
    public async Task NotifyLowCreditsAsync(int userId, double remainingCredits, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "⚠️ Low Credits Warning",
            $"You have only {remainingCredits:0.##} credits remaining. Consider purchasing more to continue using the service.",
            "/plans",
            ct
        );
        _logger.LogInformation("Low credits notification sent to user {UserId}", userId);
    }

    /// <summary>
    /// Notify user when credits run out
    /// </summary>
    public async Task NotifyOutOfCreditsAsync(int userId, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "❌ Out of Credits",
            "You've run out of credits. Purchase more credits or upgrade your plan to continue.",
            "/plans",
            ct
        );
        _logger.LogInformation("Out of credits notification sent to user {UserId}", userId);
    }

    // ============================================
    // COMPLAINT/SUPPORT NOTIFICATIONS
    // ============================================

    /// <summary>
    /// Notify user when admin responds to their complaint
    /// </summary>
    public async Task NotifyComplaintResponseAsync(int userId, int ticketId, string ticketSubject, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "💬 Support Response",
            $"You have a new response to your support ticket: \"{ticketSubject}\"",
            $"/support/ticket/{ticketId}",
            ct
        );
        _logger.LogInformation("Complaint response notification sent to user {UserId} for ticket {TicketId}", userId, ticketId);
    }

    /// <summary>
    /// Notify user when their ticket status changes
    /// </summary>
    public async Task NotifyTicketStatusChangeAsync(int userId, int ticketId, string ticketSubject, string newStatus, CancellationToken ct = default)
    {
        var statusEmoji = newStatus switch
        {
            "resolved" => "✅",
            "closed" => "🔒",
            "in_progress" => "🔄",
            _ => "📋"
        };

        await _store.CreateAsync(
            userId,
            $"{statusEmoji} Ticket Status Updated",
            $"Your ticket \"{ticketSubject}\" has been marked as {newStatus}.",
            $"/support/ticket/{ticketId}",
            ct
        );
        _logger.LogInformation("Ticket status notification sent to user {UserId} for ticket {TicketId}", userId, ticketId);
    }

    // ============================================
    // SUBSCRIPTION NOTIFICATIONS
    // ============================================

    /// <summary>
    /// Notify user when subscription is activated
    /// </summary>
    public async Task NotifySubscriptionActivatedAsync(int userId, string planName, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "🎉 Subscription Activated",
            $"Your {planName} subscription is now active. Enjoy your premium features!",
            "/plans",
            ct
        );
        _logger.LogInformation("Subscription activated notification sent to user {UserId}", userId);
    }

    /// <summary>
    /// Notify user when subscription is about to expire
    /// </summary>
    public async Task NotifySubscriptionExpiringAsync(int userId, string planName, int daysRemaining, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "⏰ Subscription Expiring Soon",
            $"Your {planName} subscription will expire in {daysRemaining} day(s). Renew now to avoid interruption.",
            "/plans",
            ct
        );
        _logger.LogInformation("Subscription expiring notification sent to user {UserId}", userId);
    }

    /// <summary>
    /// Notify user when subscription is cancelled
    /// </summary>
    public async Task NotifySubscriptionCancelledAsync(int userId, string planName, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "📋 Subscription Cancelled",
            $"Your {planName} subscription has been cancelled. You can resubscribe anytime.",
            "/plans",
            ct
        );
        _logger.LogInformation("Subscription cancelled notification sent to user {UserId}", userId);
    }

    // ============================================
    // PROMOTION NOTIFICATIONS
    // ============================================

    /// <summary>
    /// Send promotional notification to specific users
    /// </summary>
    public async Task SendPromotionAsync(IEnumerable<int> userIds, string title, string message, string? actionUrl = null, CancellationToken ct = default)
    {
        await _store.CreateManyAsync(
            userIds,
            $"🎁 {title}",
            message,
            actionUrl ?? "/plans",
            ct
        );
        _logger.LogInformation("Promotion notification sent to {Count} users", userIds.Count());
    }

    /// <summary>
    /// Send promotional notification to all users
    /// </summary>
    public async Task SendPromotionToAllAsync(string title, string message, string? actionUrl = null, CancellationToken ct = default)
    {
        var allUserIds = await _db.Users
            .AsNoTracking()
            .Select(u => u.Id)
            .ToListAsync(ct);

        await SendPromotionAsync(allUserIds, title, message, actionUrl, ct);
    }

    // ============================================
    // SYSTEM NOTIFICATIONS
    // ============================================

    /// <summary>
    /// Send system announcement to all users
    /// </summary>
    public async Task SendSystemAnnouncementAsync(string title, string message, string? actionUrl = null, CancellationToken ct = default)
    {
        var allUserIds = await _db.Users
            .AsNoTracking()
            .Select(u => u.Id)
            .ToListAsync(ct);

        await _store.CreateManyAsync(
            allUserIds,
            $"📢 {title}",
            message,
            actionUrl,
            ct
        );
        _logger.LogInformation("System announcement sent to {Count} users", allUserIds.Count);
    }

    /// <summary>
    /// Notify user about account-related changes
    /// </summary>
    public async Task NotifyAccountUpdateAsync(int userId, string updateType, string details, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            $"👤 Account {updateType}",
            details,
            "/edit-profile",
            ct
        );
        _logger.LogInformation("Account update notification sent to user {UserId}", userId);
    }

    /// <summary>
    /// Welcome notification for new users
    /// </summary>
    public async Task SendWelcomeNotificationAsync(int userId, string username, CancellationToken ct = default)
    {
        await _store.CreateAsync(
            userId,
            "👋 Welcome to Little Helper AI!",
            $"Hello {username}! Welcome aboard. Start by exploring your dashboard or check out our plans.",
            "/",
            ct
        );
        _logger.LogInformation("Welcome notification sent to user {UserId}", userId);
    }

    // ============================================
    // GENERIC NOTIFICATION
    // ============================================

    /// <summary>
    /// Send a custom notification to a user
    /// </summary>
    public async Task SendAsync(int userId, string title, string message, string? actionUrl = null, CancellationToken ct = default)
    {
        await _store.CreateAsync(userId, title, message, actionUrl, ct);
    }

    /// <summary>
    /// Send a custom notification to multiple users
    /// </summary>
    public async Task SendToManyAsync(IEnumerable<int> userIds, string title, string message, string? actionUrl = null, CancellationToken ct = default)
    {
        await _store.CreateManyAsync(userIds, title, message, actionUrl, ct);
    }
}
