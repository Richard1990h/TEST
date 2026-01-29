using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Health monitoring service for LLM engine.
/// </summary>
public interface ILlmHealthMonitor
{
    /// <summary>
    /// Current health status.
    /// </summary>
    LlmHealthStatus Status { get; }

    /// <summary>
    /// Get comprehensive health report.
    /// </summary>
    LlmHealthReport GetHealthReport();

    /// <summary>
    /// Perform a health check.
    /// </summary>
    Task<LlmHealthReport> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when health status changes.
    /// </summary>
    event Action<LlmHealthStatus, string>? OnHealthStatusChanged;

    /// <summary>
    /// Start continuous monitoring.
    /// </summary>
    void StartMonitoring(TimeSpan interval);

    /// <summary>
    /// Stop monitoring.
    /// </summary>
    void StopMonitoring();
}

/// <summary>
/// Health status levels.
/// </summary>
public enum LlmHealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
    Critical
}

/// <summary>
/// Detailed health report.
/// </summary>
public class LlmHealthReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LlmHealthStatus Status { get; set; }
    public string StatusMessage { get; set; } = "";

    // Model Health
    public bool ModelLoaded { get; set; }
    public string? ModelName { get; set; }
    public long ModelSizeBytes { get; set; }

    // Performance Metrics
    public double TokensPerSecond { get; set; }
    public long TotalTokensGenerated { get; set; }
    public TimeSpan TotalGenerationTime { get; set; }
    public TimeSpan AverageResponseTime { get; set; }

    // Memory Metrics
    public long ProcessMemoryBytes { get; set; }
    public long GcMemoryBytes { get; set; }
    public double MemoryPressurePercent { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }

    // GPU Metrics
    public GpuInfo? ActiveGpu { get; set; }
    public double GpuMemoryUsagePercent { get; set; }
    public int GpuLayersLoaded { get; set; }

    // Health Checks
    public List<HealthCheckResult> Checks { get; set; } = new();

    public string ProcessMemoryFormatted => FormatBytes(ProcessMemoryBytes);
    public string ModelSizeFormatted => FormatBytes(ModelSizeBytes);

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
/// Individual health check result.
/// </summary>
public class HealthCheckResult
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string Message { get; set; } = "";
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Health monitor implementation.
/// </summary>
public class LlmHealthMonitor : ILlmHealthMonitor, IDisposable
{
    private readonly ILogger<LlmHealthMonitor> _logger;
    private readonly ILlmEngine _engine;
    private readonly IGpuDetector _gpuDetector;
    private readonly LlmConfig _config;

    private LlmHealthStatus _status = LlmHealthStatus.Unknown;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private LlmHealthReport? _lastReport;

    // Performance tracking
    private readonly Queue<(DateTime Time, int Tokens, TimeSpan Duration)> _recentGenerations = new();
    private readonly object _metricsLock = new();
    private const int MaxRecentGenerations = 100;

    public LlmHealthStatus Status => _status;

    public event Action<LlmHealthStatus, string>? OnHealthStatusChanged;

    public LlmHealthMonitor(
        ILogger<LlmHealthMonitor> logger,
        ILlmEngine engine,
        IGpuDetector gpuDetector,
        LlmConfig config)
    {
        _logger = logger;
        _engine = engine;
        _gpuDetector = gpuDetector;
        _config = config;
    }

    public LlmHealthReport GetHealthReport()
    {
        return _lastReport ?? new LlmHealthReport { Status = LlmHealthStatus.Unknown };
    }

    public async Task<LlmHealthReport> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var report = new LlmHealthReport();
        var checks = new List<HealthCheckResult>();

        try
        {
            // 1. Model loaded check
            var modelCheck = new HealthCheckResult { Name = "ModelLoaded" };
            var sw = Stopwatch.StartNew();
            modelCheck.Passed = _engine.IsLoaded;
            modelCheck.Message = modelCheck.Passed
                ? $"Model loaded: {_engine.CurrentModel}"
                : "No model loaded";
            modelCheck.Duration = sw.Elapsed;
            checks.Add(modelCheck);

            report.ModelLoaded = _engine.IsLoaded;
            report.ModelName = _engine.CurrentModel;

            // 2. Engine health check
            var engineCheck = new HealthCheckResult { Name = "EngineHealth" };
            sw.Restart();
            engineCheck.Passed = _engine.IsHealthy;
            engineCheck.Message = engineCheck.Passed ? "Engine healthy" : "Engine unhealthy";
            engineCheck.Duration = sw.Elapsed;
            checks.Add(engineCheck);

            // 3. Memory pressure check
            var memoryCheck = await CheckMemoryHealthAsync(report);
            checks.Add(memoryCheck);

            // 4. GPU availability check
            var gpuCheck = await CheckGpuHealthAsync(report, cancellationToken);
            checks.Add(gpuCheck);

            // 5. Inference test (if model loaded and not busy)
            if (_engine.IsLoaded && !_engine.IsBusy)
            {
                var inferenceCheck = await CheckInferenceHealthAsync(cancellationToken);
                checks.Add(inferenceCheck);
            }

            // Get engine stats
            if (_engine.IsLoaded)
            {
                var stats = _engine.GetStats();
                report.ModelSizeBytes = stats.ModelSizeBytes;
                report.TokensPerSecond = stats.AverageTokensPerSecond;
                report.TotalTokensGenerated = stats.TotalTokensGenerated;
                report.TotalGenerationTime = stats.TotalGenerationTime;
                report.GpuLayersLoaded = stats.GpuLayers;
            }

            // Calculate average response time from recent generations
            lock (_metricsLock)
            {
                if (_recentGenerations.Count > 0)
                {
                    var avgMs = _recentGenerations.Average(g => g.Duration.TotalMilliseconds);
                    report.AverageResponseTime = TimeSpan.FromMilliseconds(avgMs);
                }
            }

            // Determine overall status
            report.Checks = checks;
            report.Status = DetermineOverallStatus(checks);
            report.StatusMessage = GetStatusMessage(report.Status, checks);

            // Update cached state
            _lastReport = report;
            var previousStatus = _status;
            _status = report.Status;

            if (previousStatus != _status)
            {
                _logger.LogInformation("LLM health status changed: {OldStatus} -> {NewStatus}: {Message}",
                    previousStatus, _status, report.StatusMessage);
                OnHealthStatusChanged?.Invoke(_status, report.StatusMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            report.Status = LlmHealthStatus.Critical;
            report.StatusMessage = $"Health check error: {ex.Message}";
            report.Checks.Add(new HealthCheckResult
            {
                Name = "HealthCheckError",
                Passed = false,
                Message = ex.Message
            });
        }

        return report;
    }

    private Task<HealthCheckResult> CheckMemoryHealthAsync(LlmHealthReport report)
    {
        var check = new HealthCheckResult { Name = "MemoryPressure" };
        var sw = Stopwatch.StartNew();

        try
        {
            var process = Process.GetCurrentProcess();
            report.ProcessMemoryBytes = process.WorkingSet64;
            report.GcMemoryBytes = GC.GetTotalMemory(false);
            report.Gen0Collections = GC.CollectionCount(0);
            report.Gen1Collections = GC.CollectionCount(1);
            report.Gen2Collections = GC.CollectionCount(2);

            // Check memory pressure (using working set vs typical expectations)
            var memInfo = GC.GetGCMemoryInfo();
            var totalAvailable = memInfo.TotalAvailableMemoryBytes;
            report.MemoryPressurePercent = totalAvailable > 0
                ? (report.ProcessMemoryBytes * 100.0 / totalAvailable)
                : 0;

            // Warn if using >70% of available memory
            check.Passed = report.MemoryPressurePercent < 70;
            check.Message = check.Passed
                ? $"Memory usage: {report.MemoryPressurePercent:F1}%"
                : $"High memory pressure: {report.MemoryPressurePercent:F1}%";
        }
        catch (Exception ex)
        {
            check.Passed = false;
            check.Message = $"Memory check error: {ex.Message}";
        }

        check.Duration = sw.Elapsed;
        return Task.FromResult(check);
    }

    private async Task<HealthCheckResult> CheckGpuHealthAsync(LlmHealthReport report, CancellationToken cancellationToken)
    {
        var check = new HealthCheckResult { Name = "GpuAvailability" };
        var sw = Stopwatch.StartNew();

        try
        {
            var gpu = await _gpuDetector.GetBestGpuAsync(cancellationToken);
            report.ActiveGpu = gpu;

            if (gpu != null)
            {
                report.GpuMemoryUsagePercent = gpu.MemoryUsagePercent;
                check.Passed = gpu.FreeMemoryBytes > 500 * 1024 * 1024; // At least 500MB free
                check.Message = check.Passed
                    ? $"GPU: {gpu.Name} ({gpu.FreeMemoryFormatted} free)"
                    : $"Low GPU memory: {gpu.FreeMemoryFormatted} free";
            }
            else
            {
                check.Passed = true; // CPU-only is acceptable
                check.Message = "No GPU detected, using CPU";
            }
        }
        catch (Exception ex)
        {
            check.Passed = true; // Don't fail health check for GPU detection errors
            check.Message = $"GPU check skipped: {ex.Message}";
        }

        check.Duration = sw.Elapsed;
        return check;
    }

    private async Task<HealthCheckResult> CheckInferenceHealthAsync(CancellationToken cancellationToken)
    {
        var check = new HealthCheckResult { Name = "InferenceTest" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _engine.GenerateAsync("Hi", maxTokens: 5, cancellationToken: cts.Token);

            check.Passed = !string.IsNullOrEmpty(response);
            check.Message = check.Passed
                ? $"Inference OK in {sw.ElapsedMilliseconds}ms"
                : "Inference returned empty response";

            // Track this generation
            RecordGeneration(response?.Length ?? 0, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            check.Passed = false;
            check.Message = "Inference timed out";
        }
        catch (Exception ex)
        {
            check.Passed = false;
            check.Message = $"Inference error: {ex.Message}";
        }

        check.Duration = sw.Elapsed;
        return check;
    }

    private LlmHealthStatus DetermineOverallStatus(List<HealthCheckResult> checks)
    {
        if (checks.Count == 0)
            return LlmHealthStatus.Unknown;

        var failedChecks = checks.Where(c => !c.Passed).ToList();

        if (failedChecks.Count == 0)
            return LlmHealthStatus.Healthy;

        // Critical if model not loaded or inference fails
        if (failedChecks.Any(c => c.Name is "ModelLoaded" or "EngineHealth" or "InferenceTest"))
            return LlmHealthStatus.Critical;

        // Degraded for memory or GPU issues
        if (failedChecks.Any(c => c.Name is "MemoryPressure" or "GpuAvailability"))
            return LlmHealthStatus.Degraded;

        return LlmHealthStatus.Unhealthy;
    }

    private string GetStatusMessage(LlmHealthStatus status, List<HealthCheckResult> checks)
    {
        return status switch
        {
            LlmHealthStatus.Healthy => "All systems operational",
            LlmHealthStatus.Degraded => string.Join("; ", checks.Where(c => !c.Passed).Select(c => c.Message)),
            LlmHealthStatus.Unhealthy => string.Join("; ", checks.Where(c => !c.Passed).Select(c => c.Message)),
            LlmHealthStatus.Critical => string.Join("; ", checks.Where(c => !c.Passed).Select(c => c.Message)),
            _ => "Status unknown"
        };
    }

    public void RecordGeneration(int tokens, TimeSpan duration)
    {
        lock (_metricsLock)
        {
            _recentGenerations.Enqueue((DateTime.UtcNow, tokens, duration));
            while (_recentGenerations.Count > MaxRecentGenerations)
            {
                _recentGenerations.Dequeue();
            }
        }
    }

    public void StartMonitoring(TimeSpan interval)
    {
        StopMonitoring();

        _monitorCts = new CancellationTokenSource();
        _monitorTask = MonitorLoopAsync(interval, _monitorCts.Token);
        _logger.LogInformation("Started health monitoring with {Interval} interval", interval);
    }

    public void StopMonitoring()
    {
        if (_monitorCts != null)
        {
            _monitorCts.Cancel();
            _monitorCts.Dispose();
            _monitorCts = null;
        }

        _monitorTask = null;
        _logger.LogInformation("Stopped health monitoring");
    }

    private async Task MonitorLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckHealthAsync(cancellationToken);
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health monitor loop error");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
