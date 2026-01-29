using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// Configuration for dead letter queue.
/// </summary>
public class DeadLetterQueueOptions
{
    public const string SectionName = "DeadLetterQueue";

    /// <summary>
    /// How long to retain resolved/failed messages in days.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Whether to enable the background writer.
    /// </summary>
    public bool EnableBackgroundWriter { get; set; } = true;

    /// <summary>
    /// How often to flush the queue in seconds.
    /// </summary>
    public int FlushIntervalSeconds { get; set; } = 5;
}

/// <summary>
/// Service for managing the dead letter queue.
/// </summary>
public interface IDeadLetterQueueService
{
    /// <summary>
    /// Enqueue a failed message.
    /// </summary>
    Task EnqueueAsync(CreateDeadLetterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a failed message (non-blocking, uses background writer).
    /// </summary>
    void EnqueueBackground(CreateDeadLetterRequest request);

    /// <summary>
    /// Get a message by ID.
    /// </summary>
    Task<DeadLetterMessage?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending messages for a user.
    /// </summary>
    Task<List<DeadLetterMessage>> GetPendingForUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all pending messages.
    /// </summary>
    Task<List<DeadLetterMessage>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a message as retrying.
    /// </summary>
    Task<bool> MarkRetryingAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a message as resolved.
    /// </summary>
    Task<bool> MarkResolvedAsync(string id, string? notes = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a message as failed (no more retries).
    /// </summary>
    Task<bool> MarkFailedAsync(string id, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismiss a message (manual resolution).
    /// </summary>
    Task<bool> DismissAsync(string id, string? notes = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get queue summary statistics.
    /// </summary>
    Task<DeadLetterQueueSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up old resolved/failed messages.
    /// </summary>
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get messages ready for retry.
    /// </summary>
    Task<List<DeadLetterMessage>> GetReadyForRetryAsync(int limit = 10, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of dead letter queue service.
/// </summary>
public class DeadLetterQueueService : IDeadLetterQueueService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeadLetterQueueService> _logger;
    private readonly DeadLetterQueueOptions _options;
    private readonly ConcurrentQueue<CreateDeadLetterRequest> _backgroundQueue = new();

    public DeadLetterQueueService(
        IServiceProvider serviceProvider,
        ILogger<DeadLetterQueueService> logger,
        IOptions<DeadLetterQueueOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Internal queue for background processing.
    /// </summary>
    internal ConcurrentQueue<CreateDeadLetterRequest> BackgroundQueue => _backgroundQueue;

    public async Task EnqueueAsync(CreateDeadLetterRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = new DeadLetterMessage
        {
            UserId = request.UserId,
            ChatId = request.ChatId,
            ConversationId = request.ConversationId,
            RequestPayload = request.RequestPayload,
            ErrorMessage = request.ErrorMessage,
            ErrorType = request.ErrorType,
            StackTrace = request.StackTrace,
            MaxRetries = _options.MaxRetries,
            Status = DeadLetterStatus.Pending,
            Metadata = request.Metadata != null
                ? JsonSerializer.Serialize(request.Metadata)
                : null
        };

        context.DeadLetterMessages.Add(message);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Dead letter message created: {Id} for user {UserId}, error: {ErrorType}",
            message.Id,
            message.UserId,
            message.ErrorType);
    }

    public void EnqueueBackground(CreateDeadLetterRequest request)
    {
        _backgroundQueue.Enqueue(request);
        _logger.LogDebug("Dead letter message queued for background processing");
    }

    public async Task<DeadLetterMessage?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.DeadLetterMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<List<DeadLetterMessage>> GetPendingForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.DeadLetterMessages
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == DeadLetterStatus.Pending)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DeadLetterMessage>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.DeadLetterMessages
            .AsNoTracking()
            .Where(m => m.Status == DeadLetterStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DeadLetterMessage>> GetReadyForRetryAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        // Get pending messages that are ready for retry
        var messages = await context.DeadLetterMessages
            .AsNoTracking()
            .Where(m => m.Status == DeadLetterStatus.Pending && m.RetryCount < m.MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .Take(limit * 2) // Get extra to filter
            .ToListAsync(cancellationToken);

        // Filter by retry delay
        return messages
            .Where(m => m.NextRetryDelay == null || m.NextRetryDelay <= TimeSpan.Zero)
            .Take(limit)
            .ToList();
    }

    public async Task<bool> MarkRetryingAsync(string id, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = await context.DeadLetterMessages.FindAsync(new object[] { id }, cancellationToken);
        if (message == null || !message.CanRetry)
            return false;

        message.Status = DeadLetterStatus.Retrying;
        message.RetryCount++;
        message.LastRetryAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Dead letter message {Id} marked as retrying (attempt {Attempt}/{Max})",
            id,
            message.RetryCount,
            message.MaxRetries);

        return true;
    }

    public async Task<bool> MarkResolvedAsync(string id, string? notes = null, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = await context.DeadLetterMessages.FindAsync(new object[] { id }, cancellationToken);
        if (message == null)
            return false;

        message.Status = DeadLetterStatus.Resolved;
        message.ResolvedAt = DateTime.UtcNow;
        message.ResolutionNotes = notes;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Dead letter message {Id} resolved", id);

        return true;
    }

    public async Task<bool> MarkFailedAsync(string id, string? reason = null, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = await context.DeadLetterMessages.FindAsync(new object[] { id }, cancellationToken);
        if (message == null)
            return false;

        message.Status = DeadLetterStatus.Failed;
        message.ResolvedAt = DateTime.UtcNow;
        message.ResolutionNotes = reason ?? "Max retries exceeded";

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Dead letter message {Id} permanently failed: {Reason}", id, reason);

        return true;
    }

    public async Task<bool> DismissAsync(string id, string? notes = null, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var message = await context.DeadLetterMessages.FindAsync(new object[] { id }, cancellationToken);
        if (message == null)
            return false;

        message.Status = DeadLetterStatus.Dismissed;
        message.ResolvedAt = DateTime.UtcNow;
        message.ResolutionNotes = notes ?? "Manually dismissed";

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Dead letter message {Id} dismissed", id);

        return true;
    }

    public async Task<DeadLetterQueueSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stats = await context.DeadLetterMessages
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var pending = await context.DeadLetterMessages
            .Where(m => m.Status == DeadLetterStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return new DeadLetterQueueSummary
        {
            TotalCount = stats.Sum(s => s.Count),
            PendingCount = stats.FirstOrDefault(s => s.Status == DeadLetterStatus.Pending)?.Count ?? 0,
            RetryingCount = stats.FirstOrDefault(s => s.Status == DeadLetterStatus.Retrying)?.Count ?? 0,
            ResolvedCount = stats.FirstOrDefault(s => s.Status == DeadLetterStatus.Resolved)?.Count ?? 0,
            FailedCount = stats.FirstOrDefault(s => s.Status == DeadLetterStatus.Failed)?.Count ?? 0,
            OldestPending = pending.FirstOrDefault(),
            NewestPending = pending.LastOrDefault()
        };
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);

        var toDelete = await context.DeadLetterMessages
            .Where(m =>
                (m.Status == DeadLetterStatus.Resolved || m.Status == DeadLetterStatus.Failed || m.Status == DeadLetterStatus.Dismissed) &&
                m.ResolvedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (toDelete.Count > 0)
        {
            context.DeadLetterMessages.RemoveRange(toDelete);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cleaned up {Count} old dead letter messages", toDelete.Count);
        }

        return toDelete.Count;
    }
}
