namespace LittleHelperAI.Shared.Models;

/// <summary>
/// Status of a dead letter message.
/// </summary>
public enum DeadLetterStatus
{
    /// <summary>
    /// Pending retry.
    /// </summary>
    Pending,

    /// <summary>
    /// Currently being retried.
    /// </summary>
    Retrying,

    /// <summary>
    /// Successfully reprocessed.
    /// </summary>
    Resolved,

    /// <summary>
    /// Permanently failed after max retries.
    /// </summary>
    Failed,

    /// <summary>
    /// Manually dismissed.
    /// </summary>
    Dismissed
}

/// <summary>
/// Entity for storing failed messages in the dead letter queue.
/// </summary>
public class DeadLetterMessage
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User ID who made the request.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Chat ID if applicable.
    /// </summary>
    public int? ChatId { get; set; }

    /// <summary>
    /// Conversation ID if applicable.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Original request payload as JSON.
    /// </summary>
    public string RequestPayload { get; set; } = string.Empty;

    /// <summary>
    /// Error message.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Type of error (e.g., "Timeout", "CircuitBreaker", "LlmError").
    /// </summary>
    public string ErrorType { get; set; } = string.Empty;

    /// <summary>
    /// Full stack trace if available.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum retries allowed.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Current status.
    /// </summary>
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;

    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the last retry was attempted.
    /// </summary>
    public DateTime? LastRetryAt { get; set; }

    /// <summary>
    /// When the message was resolved.
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Notes about resolution.
    /// </summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// Additional metadata as JSON.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Whether the message can be retried.
    /// </summary>
    public bool CanRetry => Status == DeadLetterStatus.Pending && RetryCount < MaxRetries;

    /// <summary>
    /// Time until next retry is allowed.
    /// </summary>
    public TimeSpan? NextRetryDelay
    {
        get
        {
            if (!CanRetry)
                return null;

            // Exponential backoff: 1min, 5min, 15min, 30min
            var delayMinutes = RetryCount switch
            {
                0 => 1,
                1 => 5,
                2 => 15,
                _ => 30
            };

            if (LastRetryAt == null)
                return TimeSpan.Zero;

            var nextRetry = LastRetryAt.Value.AddMinutes(delayMinutes);
            var remaining = nextRetry - DateTime.UtcNow;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}

/// <summary>
/// DTO for creating a dead letter message.
/// </summary>
public class CreateDeadLetterRequest
{
    public int UserId { get; set; }
    public int? ChatId { get; set; }
    public string? ConversationId { get; set; }
    public string RequestPayload { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Summary of dead letter queue status.
/// </summary>
public class DeadLetterQueueSummary
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int RetryingCount { get; set; }
    public int ResolvedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime? OldestPending { get; set; }
    public DateTime? NewestPending { get; set; }
}
