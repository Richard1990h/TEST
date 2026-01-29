using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LittleHelperAI.Data;

namespace LittleHelperAI.Backend.Services;

public sealed class ChatHistoryWriterHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ChatHistoryWriteQueue _queue;
    private readonly ILogger<ChatHistoryWriterHostedService> _logger;

    public ChatHistoryWriterHostedService(IServiceProvider sp, ChatHistoryWriteQueue queue, ILogger<ChatHistoryWriterHostedService> logger)
    {
        _sp = sp;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.ChatHistory.Add(item);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist chat history.");
            }
        }
    }
}
