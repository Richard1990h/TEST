using LittleHelperAI.Backend.Hubs;
using LittleHelperAI.KingFactory.Tools.Filesystem;
using Microsoft.AspNetCore.SignalR;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// SignalR implementation of file event notifier.
/// Broadcasts file events to connected clients.
/// </summary>
public class SignalRFileEventNotifier : IFileEventNotifier
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<SignalRFileEventNotifier> _logger;
    private readonly FilesystemConfig _config;

    public SignalRFileEventNotifier(
        IHubContext<ChatHub> hubContext,
        ILogger<SignalRFileEventNotifier> logger,
        FilesystemConfig config)
    {
        _hubContext = hubContext;
        _logger = logger;
        _config = config;
    }

    public async Task NotifyFileCreatedAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var relativePath = GetRelativePath(path);
        _logger.LogInformation("[FileEvent] File created: {Path}", relativePath);

        await _hubContext.Clients.All.SendAsync("ReceiveFileCreated", new
        {
            path = relativePath,
            content = content,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    public async Task NotifyFileUpdatedAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var relativePath = GetRelativePath(path);
        _logger.LogInformation("[FileEvent] File updated: {Path}", relativePath);

        await _hubContext.Clients.All.SendAsync("ReceiveFileUpdated", new
        {
            path = relativePath,
            content = content,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    public async Task NotifyFileDeletedAsync(string path, CancellationToken cancellationToken = default)
    {
        var relativePath = GetRelativePath(path);
        _logger.LogInformation("[FileEvent] File deleted: {Path}", relativePath);

        await _hubContext.Clients.All.SendAsync("ReceiveFileDeleted", new
        {
            path = relativePath,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    public async Task NotifyDirectoryCreatedAsync(string path, CancellationToken cancellationToken = default)
    {
        var relativePath = GetRelativePath(path);
        _logger.LogInformation("[FileEvent] Directory created: {Path}", relativePath);

        await _hubContext.Clients.All.SendAsync("ReceiveDirectoryCreated", new
        {
            path = relativePath,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    private string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(_config.BaseDirectory))
            return fullPath;

        try
        {
            return Path.GetRelativePath(_config.BaseDirectory, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }
}
