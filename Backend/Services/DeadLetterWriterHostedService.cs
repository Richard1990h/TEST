using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// Background service that writes dead letter messages asynchronously.
/// </summary>
public class DeadLetterWriterHostedService : BackgroundService
{
    private readonly ILogger<DeadLetterWriterHostedService> _logger;
    private readonly IDeadLetterQueueService _deadLetterQueue;
    private readonly DeadLetterQueueOptions _options;

    public DeadLetterWriterHostedService(
        ILogger<DeadLetterWriterHostedService> logger,
        IDeadLetterQueueService deadLetterQueue,
        IOptions<DeadLetterQueueOptions> options)
    {
        _logger = logger;
        _deadLetterQueue = deadLetterQueue;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundWriter)
        {
            _logger.LogInformation("Dead letter background writer is disabled");
            return;
        }

        _logger.LogInformation("Dead letter background writer started");

        var flushInterval = TimeSpan.FromSeconds(_options.FlushIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushQueueAsync(stoppingToken);
                await Task.Delay(flushInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in dead letter writer");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        // Final flush on shutdown
        try
        {
            await FlushQueueAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during final dead letter flush");
        }

        _logger.LogInformation("Dead letter background writer stopped");
    }

    private async Task FlushQueueAsync(CancellationToken cancellationToken)
    {
        // Access the internal queue
        if (_deadLetterQueue is not DeadLetterQueueService service)
            return;

        var count = 0;
        while (service.BackgroundQueue.TryDequeue(out var request))
        {
            try
            {
                await _deadLetterQueue.EnqueueAsync(request, cancellationToken);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist dead letter message for user {UserId}", request.UserId);
                // Re-queue for retry
                service.EnqueueBackground(request);
                break; // Avoid infinite loop
            }
        }

        if (count > 0)
        {
            _logger.LogDebug("Flushed {Count} dead letter messages to database", count);
        }
    }
}
