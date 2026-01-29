using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Memory management service for LLM engine.
/// </summary>
public interface ILlmMemoryManager
{
    /// <summary>
    /// Current memory usage statistics.
    /// </summary>
    MemoryStats GetMemoryStats();

    /// <summary>
    /// Perform garbage collection and memory cleanup.
    /// </summary>
    MemoryCleanupResult PerformCleanup(bool aggressive = false);

    /// <summary>
    /// Check if memory pressure is high.
    /// </summary>
    bool IsMemoryPressureHigh { get; }

    /// <summary>
    /// Set memory thresholds for automatic cleanup.
    /// </summary>
    void SetThresholds(long warningThresholdBytes, long criticalThresholdBytes);

    /// <summary>
    /// Event fired when memory pressure is detected.
    /// </summary>
    event Action<MemoryPressureLevel, long>? OnMemoryPressure;
}

/// <summary>
/// Memory pressure levels.
/// </summary>
public enum MemoryPressureLevel
{
    Normal,
    Warning,
    Critical
}

/// <summary>
/// Memory statistics.
/// </summary>
public class MemoryStats
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Process memory
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public long VirtualMemoryBytes { get; set; }
    public long PagedMemoryBytes { get; set; }

    // GC memory
    public long GcTotalMemoryBytes { get; set; }
    public long GcHeapSizeBytes { get; set; }
    public long GcFragmentedBytes { get; set; }
    public long GcPinnedObjectsCount { get; set; }

    // GC collections
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public TimeSpan TotalPauseTime { get; set; }
    public double GcPauseTimePercent { get; set; }

    // System memory
    public long TotalAvailableMemoryBytes { get; set; }
    public long HighMemoryThresholdBytes { get; set; }
    public MemoryPressureLevel PressureLevel { get; set; }

    // Formatted strings
    public string WorkingSetFormatted => FormatBytes(WorkingSetBytes);
    public string GcTotalFormatted => FormatBytes(GcTotalMemoryBytes);
    public string TotalAvailableFormatted => FormatBytes(TotalAvailableMemoryBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Result of memory cleanup operation.
/// </summary>
public class MemoryCleanupResult
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }

    public long MemoryBefore { get; set; }
    public long MemoryAfter { get; set; }
    public long MemoryFreed => MemoryBefore - MemoryAfter;

    public int Gen0CollectionsBefore { get; set; }
    public int Gen0CollectionsAfter { get; set; }
    public int Gen1CollectionsBefore { get; set; }
    public int Gen1CollectionsAfter { get; set; }
    public int Gen2CollectionsBefore { get; set; }
    public int Gen2CollectionsAfter { get; set; }

    public bool WasAggressive { get; set; }
    public string Message { get; set; } = "";

    public string MemoryFreedFormatted => FormatBytes(MemoryFreed);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Memory manager implementation.
/// </summary>
public class LlmMemoryManager : ILlmMemoryManager
{
    private readonly ILogger<LlmMemoryManager> _logger;
    private readonly ILlmEngine _engine;

    private long _warningThresholdBytes = 2L * 1024 * 1024 * 1024; // 2GB
    private long _criticalThresholdBytes = 4L * 1024 * 1024 * 1024; // 4GB

    private int _lastGen0 = 0;
    private int _lastGen1 = 0;
    private int _lastGen2 = 0;
    private DateTime _lastCheck = DateTime.UtcNow;

    public event Action<MemoryPressureLevel, long>? OnMemoryPressure;

    public bool IsMemoryPressureHigh => GetMemoryStats().PressureLevel >= MemoryPressureLevel.Warning;

    public LlmMemoryManager(ILogger<LlmMemoryManager> logger, ILlmEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    public void SetThresholds(long warningThresholdBytes, long criticalThresholdBytes)
    {
        _warningThresholdBytes = warningThresholdBytes;
        _criticalThresholdBytes = criticalThresholdBytes;
        _logger.LogInformation("Memory thresholds set: Warning={Warning}MB, Critical={Critical}MB",
            warningThresholdBytes / (1024 * 1024),
            criticalThresholdBytes / (1024 * 1024));
    }

    public MemoryStats GetMemoryStats()
    {
        var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();

        var stats = new MemoryStats
        {
            // Process memory
            WorkingSetBytes = process.WorkingSet64,
            PrivateMemoryBytes = process.PrivateMemorySize64,
            VirtualMemoryBytes = process.VirtualMemorySize64,
            PagedMemoryBytes = process.PagedMemorySize64,

            // GC memory
            GcTotalMemoryBytes = GC.GetTotalMemory(false),
            GcHeapSizeBytes = gcInfo.HeapSizeBytes,
            GcFragmentedBytes = gcInfo.FragmentedBytes,
            GcPinnedObjectsCount = gcInfo.PinnedObjectsCount,

            // Collections
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            TotalPauseTime = gcInfo.PauseTimePercentage > 0
                ? TimeSpan.FromMilliseconds(gcInfo.PauseTimePercentage)
                : TimeSpan.Zero,
            GcPauseTimePercent = gcInfo.PauseTimePercentage,

            // System
            TotalAvailableMemoryBytes = gcInfo.TotalAvailableMemoryBytes,
            HighMemoryThresholdBytes = gcInfo.HighMemoryLoadThresholdBytes
        };

        // Determine pressure level
        var workingSet = stats.WorkingSetBytes;
        if (workingSet >= _criticalThresholdBytes)
        {
            stats.PressureLevel = MemoryPressureLevel.Critical;
        }
        else if (workingSet >= _warningThresholdBytes)
        {
            stats.PressureLevel = MemoryPressureLevel.Warning;
        }
        else
        {
            stats.PressureLevel = MemoryPressureLevel.Normal;
        }

        // Check if pressure changed
        CheckPressureAndNotify(stats);

        return stats;
    }

    private void CheckPressureAndNotify(MemoryStats stats)
    {
        var timeSinceLastCheck = DateTime.UtcNow - _lastCheck;
        if (timeSinceLastCheck < TimeSpan.FromSeconds(5))
            return;

        _lastCheck = DateTime.UtcNow;

        if (stats.PressureLevel >= MemoryPressureLevel.Warning)
        {
            _logger.LogWarning("Memory pressure detected: {Level}, Working Set: {Memory}",
                stats.PressureLevel, stats.WorkingSetFormatted);
            OnMemoryPressure?.Invoke(stats.PressureLevel, stats.WorkingSetBytes);
        }

        // Log if significant GC activity
        var gen2Delta = stats.Gen2Collections - _lastGen2;
        if (gen2Delta > 0)
        {
            _logger.LogDebug("Gen2 GC occurred ({Count} total), current memory: {Memory}",
                stats.Gen2Collections, stats.GcTotalFormatted);
        }

        _lastGen0 = stats.Gen0Collections;
        _lastGen1 = stats.Gen1Collections;
        _lastGen2 = stats.Gen2Collections;
    }

    public MemoryCleanupResult PerformCleanup(bool aggressive = false)
    {
        var sw = Stopwatch.StartNew();
        var result = new MemoryCleanupResult
        {
            WasAggressive = aggressive,
            MemoryBefore = GC.GetTotalMemory(false),
            Gen0CollectionsBefore = GC.CollectionCount(0),
            Gen1CollectionsBefore = GC.CollectionCount(1),
            Gen2CollectionsBefore = GC.CollectionCount(2)
        };

        try
        {
            _logger.LogInformation("Starting memory cleanup (aggressive={Aggressive}), before: {Memory}",
                aggressive, FormatBytes(result.MemoryBefore));

            if (aggressive)
            {
                // Aggressive cleanup: full blocking collection + LOH compaction
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            }
            else
            {
                // Standard cleanup: optimized collection
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                GC.WaitForPendingFinalizers();
                GC.Collect(1, GCCollectionMode.Optimized, false);
            }

            result.MemoryAfter = GC.GetTotalMemory(true);
            result.Gen0CollectionsAfter = GC.CollectionCount(0);
            result.Gen1CollectionsAfter = GC.CollectionCount(1);
            result.Gen2CollectionsAfter = GC.CollectionCount(2);
            result.Success = true;
            result.Message = $"Freed {result.MemoryFreedFormatted}";

            _logger.LogInformation("Memory cleanup complete: freed {Freed}, after: {Memory}, duration: {Duration}ms",
                result.MemoryFreedFormatted,
                FormatBytes(result.MemoryAfter),
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Cleanup failed: {ex.Message}";
            _logger.LogError(ex, "Memory cleanup failed");
        }

        result.Duration = sw.Elapsed;
        return result;
    }

    /// <summary>
    /// Trim native memory if model supports it.
    /// </summary>
    public async Task TrimNativeMemoryAsync()
    {
        try
        {
            // Request native memory release
            if (RuntimeInformationHelper.IsWindowsPlatform())
            {
                // On Windows, we can call SetProcessWorkingSetSize to trim
                var process = Process.GetCurrentProcess();
                var before = process.WorkingSet64;

                // This is a hint to the OS to trim working set
#pragma warning disable CA1416 // Platform compatibility - already checked above
                process.MinWorkingSet = (IntPtr)(-1);
                process.MaxWorkingSet = (IntPtr)(-1);
#pragma warning restore CA1416

                await Task.Delay(100); // Give OS time to respond
                process.Refresh();

                var after = process.WorkingSet64;
                _logger.LogDebug("Native memory trim: before={Before}, after={After}, freed={Freed}",
                    FormatBytes(before), FormatBytes(after), FormatBytes(before - after));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Native memory trim failed (non-critical)");
        }
    }

    /// <summary>
    /// Check if model unload is recommended due to memory pressure.
    /// </summary>
    public bool ShouldUnloadModel()
    {
        var stats = GetMemoryStats();

        // Recommend unload if critical pressure and model is loaded but idle
        if (stats.PressureLevel == MemoryPressureLevel.Critical && _engine.IsLoaded && !_engine.IsBusy)
        {
            _logger.LogWarning("Recommending model unload due to critical memory pressure");
            return true;
        }

        return false;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Runtime information helper for cross-platform compatibility.
/// </summary>
internal static class RuntimeInformationHelper
{
    public static bool IsWindowsPlatform()
    {
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);
    }
}
