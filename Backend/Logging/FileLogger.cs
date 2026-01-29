using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LittleHelperAI.Backend.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _filePath, _writeLock));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock;

    public FileLogger(string categoryName, string filePath, SemaphoreSlim writeLock)
    {
        _categoryName = categoryName;
        _filePath = filePath;
        _writeLock = writeLock;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] [{logLevel}] [{_categoryName}] {message}";
        if (exception != null)
        {
            line += $"{Environment.NewLine}{exception}";
        }

        WriteLine(line);
    }

    private void WriteLine(string line)
    {
        _writeLock.Wait();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");
            File.AppendAllText(_filePath, line + Environment.NewLine);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
