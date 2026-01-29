using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Backend.Helpers;
using LittleHelperAI.Models;
using LittleHelperAI.Data;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Backend.Infrastructure;
using LittleHelperAI.Backend.Infrastructure.RateLimiting;
using LittleHelperAI.KingFactory;
using System.Text;
using System.Text.Json;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly KnowledgeStoreService _knowledgeStore;
    private readonly CreditPolicyService _creditPolicy;
    private readonly IFactory _factory;
    private readonly IRequestTimeoutService _timeoutService;
    private readonly ILlmCircuitBreaker _circuitBreaker;
    private readonly IUserRateLimiter _rateLimiter;
    private readonly IDeadLetterQueueService _deadLetterQueue;
    private readonly IPipelineMetrics _metrics;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        ApplicationDbContext context,
        KnowledgeStoreService knowledgeStore,
        CreditPolicyService creditPolicy,
        IFactory factory,
        IRequestTimeoutService timeoutService,
        ILlmCircuitBreaker circuitBreaker,
        IUserRateLimiter rateLimiter,
        IDeadLetterQueueService deadLetterQueue,
        IPipelineMetrics metrics,
        ILogger<ChatController> logger)
    {
        _context = context;
        _knowledgeStore = knowledgeStore;
        _creditPolicy = creditPolicy;
        _factory = factory;
        _timeoutService = timeoutService;
        _circuitBreaker = circuitBreaker;
        _rateLimiter = rateLimiter;
        _deadLetterQueue = deadLetterQueue;
        _metrics = metrics;
        _logger = logger;
    }

    private static string NormalizeQuestion(string input)
    {
        return input
            .Trim()
            .ToLowerInvariant()
            .Replace("?", "")
            .Replace(".", "")
            .Replace(",", "");
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static ChatTranscript LoadTranscript(ChatHistory chat)
    {
        if (string.IsNullOrWhiteSpace(chat.Metadata))
            return new ChatTranscript();

        try
        {
            var transcript = JsonSerializer.Deserialize<ChatTranscript>(chat.Metadata, _jsonOpts);
            if (transcript?.Turns != null)
                return transcript;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChatController] LoadTranscript deserialization failed for chat {chat.Id}: {ex.Message}");
        }

        return new ChatTranscript();
    }

    private static void SaveTranscript(ChatHistory chat, ChatTranscript transcript)
    {
        chat.Metadata = JsonSerializer.Serialize(transcript, _jsonOpts);
    }

    [HttpPost("send")]
    [RequestSizeLimit(52428800)]
    public async Task<IActionResult> Send([FromBody] ChatRequest request)
    {
        if ((string.IsNullOrWhiteSpace(request.Message) && (request.Files == null || !request.Files.Any()))
            || request.UserId <= 0)
            return BadRequest("Invalid request.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (user == null)
            return Unauthorized();

        // Get user's plan for rate limiting
        var userPlan = await _context.UserPlans
            .Where(p => p.UserId == user.Id)
            .Include(p => p.Plan)
            .OrderByDescending(p => p.PurchasedAt)
            .Select(p => p.Plan.PlanTier)
            .FirstOrDefaultAsync();

        // Check rate limit
        var (rateLimitResult, rateLimitReleaser) = _rateLimiter.TryAcquire(user.Id, userPlan);
        if (!rateLimitResult.IsAllowed)
        {
            _metrics.RecordRateLimited(user.Id);
            Response.Headers["Retry-After"] = ((int)rateLimitResult.RetryAfter.TotalSeconds).ToString();
            Response.Headers["X-RateLimit-Limit"] = rateLimitResult.Limit.ToString();
            Response.Headers["X-RateLimit-Remaining"] = rateLimitResult.Remaining.ToString();
            return StatusCode(429, new { error = rateLimitResult.RejectionReason, retryAfter = rateLimitResult.RetryAfter.TotalSeconds });
        }

        // Check circuit breaker
        if (!_circuitBreaker.AllowRequest())
        {
            _metrics.RecordCircuitBreakerRejection();
            var retryAfter = _circuitBreaker.GetRetryAfter() ?? TimeSpan.FromSeconds(30);
            Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
            return StatusCode(503, new { error = "Service temporarily unavailable. Please try again later.", retryAfter = retryAfter.TotalSeconds });
        }

        _metrics.IncrementActiveRequests();
        var startTime = DateTime.UtcNow;

        try
        {
            using var timeoutContext = _timeoutService.CreateContext();
            using (rateLimitReleaser)
            {
                var rawMessage = (request.Message ?? "").Trim();
                var hasFiles = request.Files != null && request.Files.Any();
                var normalizedKey = NormalizeQuestion(rawMessage);

                ChatHistory chat;
                if (request.CreateNewChat)
                {
                    chat = new ChatHistory
                    {
                        UserId = user.Id,
                        Message = "",
                        Reply = "",
                        Title = rawMessage.Length > 50 ? rawMessage[..47] + "..." : rawMessage,
                        Timestamp = DateTime.UtcNow
                    };
                    _context.ChatHistory.Add(chat);
                }
                else
                {
                    if (!request.ChatId.HasValue || request.ChatId.Value <= 0)
                        return BadRequest("ChatId is required to continue conversation.");

                    var foundChat = await _context.ChatHistory
                        .FirstOrDefaultAsync(c => c.Id == request.ChatId.Value && c.UserId == user.Id);

                    if (foundChat == null)
                        return BadRequest("Invalid ChatId");

                    chat = foundChat;
                }

                var transcript = LoadTranscript(chat);
                transcript.Turns.Add(new ChatTurn
                {
                    Role = "user",
                    Text = rawMessage,
                    Utc = DateTime.UtcNow
                });

                // Initialize factory if not ready
                if (!_factory.IsReady)
                {
                    try
                    {
                        await _factory.InitializeAsync(timeoutContext.RequestToken);
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new { error = $"Failed to initialize AI: {ex.Message}" });
                    }
                }

                // Process through Factory with tools enabled
                var responseBuilder = new StringBuilder();
                var options = new FactoryOptions
                {
                    EnableTools = true,
                    EnableReasoning = true,
                    EnableValidation = true
                };

                await foreach (var output in _factory.ProcessInConversationAsync(
                    chat.Id.ToString(),
                    rawMessage,
                    options,
                    timeoutContext.RequestToken))
                {
                    if (output.Type == FactoryOutputType.Token)
                    {
                        responseBuilder.Append(output.Content);
                    }
                    else if (output.Type == FactoryOutputType.Complete && !string.IsNullOrEmpty(output.Content))
                    {
                        responseBuilder.Append(output.Content);
                    }
                    else if (output.Type == FactoryOutputType.Error)
                    {
                        responseBuilder.AppendLine($"\n[Error: {output.Content}]");
                    }
                    else if (output.Type == FactoryOutputType.ToolResult)
                    {
                        responseBuilder.AppendLine($"\n[Tool Result: {output.Content}]");
                    }
                }

                string reply = responseBuilder.ToString();
                if (string.IsNullOrWhiteSpace(reply))
                {
                    reply = "[No response generated]";
                }

                transcript.Turns.Add(new ChatTurn
                {
                    Role = "assistant",
                    Text = reply,
                    Utc = DateTime.UtcNow
                });

                SaveTranscript(chat, transcript);

                chat.Message = rawMessage;
                chat.Reply = reply;
                chat.Timestamp = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _circuitBreaker.RecordSuccess();
                var durationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                _metrics.RecordRequest("chat", true, durationMs);

                return Ok(new
                {
                    Message = reply,
                    MessageId = chat.Id,
                    used = 0,
                    creditsLeft = user.Credits,
                    tokens = TokenCounter.CountTokens(reply)
                });
            }
        }
        catch (RequestTimeoutException ex)
        {
            _logger.LogWarning(ex, "Request timeout for user {UserId}", user.Id);
            _circuitBreaker.RecordFailure(ex);
            var durationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _metrics.RecordTimeout("chat", durationMs);

            // Queue for dead letter
            _deadLetterQueue.EnqueueBackground(new CreateDeadLetterRequest
            {
                UserId = user.Id,
                ChatId = request.ChatId,
                RequestPayload = JsonSerializer.Serialize(request, _jsonOpts),
                ErrorMessage = ex.Message,
                ErrorType = "Timeout",
                Metadata = new Dictionary<string, object> { ["elapsedMs"] = ex.ElapsedTime.TotalMilliseconds }
            });

            return StatusCode(504, new { error = "Request timed out. Please try again.", elapsedSeconds = ex.ElapsedTime.TotalSeconds });
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker open for user {UserId}", user.Id);
            Response.Headers["Retry-After"] = ((int)ex.RetryAfter.TotalSeconds).ToString();
            return StatusCode(503, new { error = ex.Message, retryAfter = ex.RetryAfter.TotalSeconds });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "Request was cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI processing failed for user {UserId}", user.Id);
            _circuitBreaker.RecordFailure(ex);
            var durationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _metrics.RecordRequest("chat", false, durationMs);

            // Queue for dead letter
            _deadLetterQueue.EnqueueBackground(new CreateDeadLetterRequest
            {
                UserId = user.Id,
                ChatId = request.ChatId,
                RequestPayload = JsonSerializer.Serialize(request, _jsonOpts),
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                StackTrace = ex.StackTrace
            });

            return StatusCode(500, new { error = $"AI processing failed: {ex.Message}" });
        }
        finally
        {
            _metrics.DecrementActiveRequests();
        }
    }

    [HttpGet("history/{userId}")]
    public async Task<IActionResult> GetHistory(int userId)
    {
        var messages = await _context.ChatHistory
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Id)
            .Take(50)
            .Select(m => new ChatSummary
            {
                Id = m.Id,
                Title = m.Title,
                Timestamp = m.Timestamp
            })
            .ToListAsync();

        return Ok(messages);
    }

    [HttpGet("load/{chatId}")]
    public async Task<ActionResult<List<ChatMessageDto>>> LoadChatById(int chatId)
    {
        var chat = await _context.ChatHistory
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat == null)
            return NotFound();

        var messages = new List<ChatMessageDto>();

        if (!string.IsNullOrWhiteSpace(chat.Metadata))
        {
            try
            {
                var transcript = JsonSerializer.Deserialize<ChatTranscript>(
                    chat.Metadata,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (transcript?.Turns != null && transcript.Turns.Count > 0)
                {
                    foreach (var turn in transcript.Turns)
                    {
                        messages.Add(new ChatMessageDto
                        {
                            IsUser = turn.Role == "user",
                            Text = turn.Text,
                            Timestamp = turn.Utc
                        });
                    }
                    return Ok(messages);
                }
            }
            catch { }
        }

        messages.Add(new ChatMessageDto
        {
            Text = chat.Message ?? string.Empty,
            IsUser = true,
            Timestamp = chat.Timestamp
        });

        if (!string.IsNullOrWhiteSpace(chat.Reply))
        {
            messages.Add(new ChatMessageDto
            {
                Text = chat.Reply,
                IsUser = false,
                Timestamp = chat.Timestamp
            });
        }

        return Ok(messages);
    }

    [HttpDelete("delete/{chatId}")]
    public async Task<IActionResult> DeleteChat(int chatId, [FromQuery] int userId)
    {
        if (userId <= 0)
            return BadRequest("Invalid user");

        var chat = await _context.ChatHistory
            .FirstOrDefaultAsync(c => c.Id == chatId && c.UserId == userId);

        if (chat == null)
            return NotFound();

        _context.ChatHistory.Remove(chat);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, deletedChatId = chatId });
    }
}

/// <summary>
/// Admin controller for metrics and monitoring.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminMetricsController : ControllerBase
{
    private readonly IPipelineMetrics _metrics;
    private readonly ILlmCircuitBreaker _circuitBreaker;
    private readonly IDeadLetterQueueService _deadLetterQueue;

    public AdminMetricsController(
        IPipelineMetrics metrics,
        ILlmCircuitBreaker circuitBreaker,
        IDeadLetterQueueService deadLetterQueue)
    {
        _metrics = metrics;
        _circuitBreaker = circuitBreaker;
        _deadLetterQueue = deadLetterQueue;
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics([FromQuery] int windowMinutes = 60)
    {
        var window = TimeSpan.FromMinutes(windowMinutes);
        var snapshot = _metrics.GetSnapshot(window);
        return Ok(snapshot);
    }

    [HttpGet("circuit-breaker")]
    public IActionResult GetCircuitBreakerStatus()
    {
        return Ok(_circuitBreaker.GetStats());
    }

    [HttpGet("dead-letter/summary")]
    public async Task<IActionResult> GetDeadLetterSummary()
    {
        var summary = await _deadLetterQueue.GetSummaryAsync();
        return Ok(summary);
    }

    [HttpGet("dead-letter/pending")]
    public async Task<IActionResult> GetPendingDeadLetters([FromQuery] int limit = 50)
    {
        var messages = await _deadLetterQueue.GetPendingAsync(limit);
        return Ok(messages);
    }

    [HttpPost("dead-letter/{id}/dismiss")]
    public async Task<IActionResult> DismissDeadLetter(string id, [FromBody] DismissRequest? request = null)
    {
        var success = await _deadLetterQueue.DismissAsync(id, request?.Notes);
        return success ? Ok() : NotFound();
    }
}

public class DismissRequest
{
    public string? Notes { get; set; }
}
