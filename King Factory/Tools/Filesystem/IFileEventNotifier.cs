namespace LittleHelperAI.KingFactory.Tools.Filesystem;

/// <summary>
/// Interface for notifying about file system events.
/// </summary>
public interface IFileEventNotifier
{
    /// <summary>
    /// Notify that a file was created.
    /// </summary>
    Task NotifyFileCreatedAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify that a file was updated.
    /// </summary>
    Task NotifyFileUpdatedAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify that a file was deleted.
    /// </summary>
    Task NotifyFileDeletedAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify that a directory was created.
    /// </summary>
    Task NotifyDirectoryCreatedAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Null implementation for when no notification is needed.
/// </summary>
public class NullFileEventNotifier : IFileEventNotifier
{
    public Task NotifyFileCreatedAsync(string path, string content, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyFileUpdatedAsync(string path, string content, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyFileDeletedAsync(string path, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyDirectoryCreatedAsync(string path, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
