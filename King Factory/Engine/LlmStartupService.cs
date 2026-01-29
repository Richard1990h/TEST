using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Background service that handles LLM initialization on startup.
/// </summary>
public interface ILlmStartupService
{
    /// <summary>
    /// Current initialization status.
    /// </summary>
    LlmInitStatus Status { get; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    int Progress { get; }

    /// <summary>
    /// Current status message.
    /// </summary>
    string StatusMessage { get; }

    /// <summary>
    /// Event fired when status changes.
    /// </summary>
    event Action<LlmInitStatus, string>? OnStatusChanged;

    /// <summary>
    /// Event fired when progress changes.
    /// </summary>
    event Action<int, string>? OnProgressChanged;

    /// <summary>
    /// Wait for initialization to complete.
    /// </summary>
    Task WaitForReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually trigger initialization.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed status information.
    /// </summary>
    LlmInitInfo GetDetailedStatus();
}

/// <summary>
/// LLM initialization status.
/// </summary>
public enum LlmInitStatus
{
    NotStarted,
    DetectingHardware,
    ScanningModels,
    ValidatingModel,
    LoadingModel,
    Warmup,
    Ready,
    Failed,
    NoModelFound
}

/// <summary>
/// Detailed initialization information.
/// </summary>
public class LlmInitInfo
{
    public LlmInitStatus Status { get; set; }
    public int Progress { get; set; }
    public string StatusMessage { get; set; } = "";
    public TimeSpan ElapsedTime { get; set; }
    public GpuInfo? SelectedGpu { get; set; }
    public ModelInfo? SelectedModel { get; set; }
    public int GpuLayers { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> AvailableModels { get; set; } = new();
    public LlmEngineStats? EngineStats { get; set; }
}

/// <summary>
/// LLM startup service implementation.
/// </summary>
public class LlmStartupService : BackgroundService, ILlmStartupService
{
    private readonly ILogger<LlmStartupService> _logger;
    private readonly ILlmEngine _engine;
    private readonly IGpuDetector _gpuDetector;
    private readonly IModelDiscovery _modelDiscovery;
    private readonly LlmConfig _config;

    private readonly TaskCompletionSource _readyTcs = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private LlmInitStatus _status = LlmInitStatus.NotStarted;
    private int _progress;
    private string _statusMessage = "Waiting to start...";
    private GpuInfo? _selectedGpu;
    private ModelInfo? _selectedModel;
    private string? _errorMessage;
    private Stopwatch _stopwatch = new();
    private bool _initialized;

    public LlmInitStatus Status => _status;
    public int Progress => _progress;
    public string StatusMessage => _statusMessage;

    public event Action<LlmInitStatus, string>? OnStatusChanged;
    public event Action<int, string>? OnProgressChanged;

    public LlmStartupService(
        ILogger<LlmStartupService> logger,
        ILlmEngine engine,
        IGpuDetector gpuDetector,
        IModelDiscovery modelDiscovery,
        LlmConfig config)
    {
        _logger = logger;
        _engine = engine;
        _gpuDetector = gpuDetector;
        _modelDiscovery = modelDiscovery;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app fully start
        await Task.Delay(500, stoppingToken);

        try
        {
            await InitializeAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LLM initialization cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM initialization failed");
            SetStatus(LlmInitStatus.Failed, $"Initialization failed: {ex.Message}");
            _errorMessage = ex.Message;
            _readyTcs.TrySetException(ex);
        }
    }

    public async Task WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waitTask = _readyTcs.Task;
        var cancelTask = Task.Delay(Timeout.Infinite, cts.Token);

        var completed = await Task.WhenAny(waitTask, cancelTask);
        if (completed == cancelTask)
        {
            throw new OperationCanceledException("Timed out waiting for LLM initialization");
        }

        await waitTask; // Propagate any exception
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized && _engine.IsLoaded)
        {
            _logger.LogDebug("LLM already initialized");
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && _engine.IsLoaded)
                return;

            _stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Starting LLM initialization...");

            // Step 1: Detect hardware
            SetStatus(LlmInitStatus.DetectingHardware, "Detecting GPU capabilities...");
            SetProgress(5, "Detecting hardware...");

            var gpus = await _gpuDetector.DetectGpusAsync(cancellationToken);
            _selectedGpu = await _gpuDetector.GetBestGpuAsync(cancellationToken);

            if (_selectedGpu != null)
            {
                _logger.LogInformation("Selected GPU: {Name} with {Vram} VRAM",
                    _selectedGpu.Name, _selectedGpu.FreeMemoryFormatted);
                SetProgress(15, $"Found GPU: {_selectedGpu.Name}");
            }
            else
            {
                _logger.LogWarning("No suitable GPU found, will use CPU");
                SetProgress(15, "No GPU detected, using CPU");
            }

            if (!_gpuDetector.IsCudaAvailable)
            {
                if (_config.GpuLayerCount != 0)
                {
                    _logger.LogInformation("CUDA not available; forcing GPU layers to 0");
                }
                _config.GpuLayerCount = 0;
            }

            if (_config.Threads == 0)
            {
                _config.Threads = (uint)Math.Max(1, Environment.ProcessorCount);
                _logger.LogInformation("Auto-detected CPU threads: {Threads}", _config.Threads);
            }

            if (_config.BatchSize == 0)
            {
                _config.BatchSize = _selectedGpu?.SupportsCuda == true ? 512u : 128u;
                _logger.LogInformation("Auto-detected batch size: {BatchSize}", _config.BatchSize);
            }

            // Step 2: Find model
            SetStatus(LlmInitStatus.ScanningModels, "Scanning for models...");
            SetProgress(25, "Scanning for models...");

            // Check explicit model path first
            string? modelPath = null;
            if (!string.IsNullOrEmpty(_config.ModelPath) && File.Exists(_config.ModelPath))
            {
                modelPath = _config.ModelPath;
                _logger.LogInformation("Using configured model: {Path}", modelPath);
            }
            else
            {
                // Auto-discover best model
                var availableVram = _selectedGpu?.FreeMemoryBytes ?? 0;
                _selectedModel = await _modelDiscovery.FindBestModelAsync(availableVram, cancellationToken);

                if (_selectedModel != null)
                {
                    modelPath = _selectedModel.Path;
                    _logger.LogInformation("Auto-selected model: {Name} ({Size})",
                        _selectedModel.FileName, _selectedModel.SizeFormatted);
                }
            }

            if (string.IsNullOrEmpty(modelPath))
            {
                SetStatus(LlmInitStatus.NoModelFound, "No model found");
                SetProgress(100, "No model found - please add a .gguf model");
                _logger.LogWarning("No model found in search paths");
                _readyTcs.TrySetResult();
                return;
            }

            // Step 3: Validate model
            SetStatus(LlmInitStatus.ValidatingModel, "Validating model...");
            SetProgress(35, "Validating model...");

            var validation = await _modelDiscovery.ValidateModelAsync(modelPath, cancellationToken);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"Model validation failed: {validation.Message}");
            }

            _selectedModel = validation.ModelInfo;
            SetProgress(45, $"Model valid: {_selectedModel?.FileName}");

            // Step 4: Configure and load model
            SetStatus(LlmInitStatus.LoadingModel, "Loading model into memory...");
            SetProgress(50, "Loading model (this may take a moment)...");

            // Calculate optimal GPU layers
            int gpuLayers = _config.GpuLayerCount; // Use config default
            if (_selectedGpu != null && _selectedModel != null && gpuLayers == -1)
            {
                // Only calculate if set to auto (-1)
                gpuLayers = _gpuDetector.CalculateOptimalGpuLayers(
                    _selectedModel.SizeBytes,
                    _selectedGpu.FreeMemoryBytes,
                    _selectedModel.LayerCount);

                _logger.LogInformation("Calculated optimal GPU layers: {Layers} (of {Total})",
                    gpuLayers, _selectedModel.LayerCount);

                // Update config BEFORE loading
                _config.GpuLayerCount = gpuLayers;
            }
            else
            {
                _logger.LogInformation("Using configured GPU layers: {Layers}",
                    gpuLayers == -1 ? "all" : gpuLayers.ToString());
            }

            // Load the model (config is already updated with optimal settings)
            SetProgress(60, "Loading weights...");

            // Update config model path so the engine loads from the correct path
            _config.ModelPath = modelPath;
            await _engine.LoadModelAsync(cancellationToken: cancellationToken);
            SetProgress(85, "Model loaded");

            // Verify model is actually loaded before warmup
            if (!_engine.IsLoaded)
            {
                throw new InvalidOperationException("Model failed to load - engine reports not loaded");
            }

            // Step 5: Warmup
            SetStatus(LlmInitStatus.Warmup, "Warming up model...");
            SetProgress(90, "Warming up...");

            await WarmupModelAsync(cancellationToken);
            SetProgress(95, "Warmup complete");

            // Done!
            _stopwatch.Stop();
            SetStatus(LlmInitStatus.Ready, "Ready");
            SetProgress(100, $"Ready in {_stopwatch.Elapsed.TotalSeconds:F1}s");

            _initialized = true;
            _logger.LogInformation("LLM initialization complete in {Time}s", _stopwatch.Elapsed.TotalSeconds);

            // Log performance info
            var stats = _engine.GetStats();
            _logger.LogInformation("Model: {Model}, Context: {Context}, GPU Layers: {Layers}",
                stats.ModelName, stats.ContextSize, stats.GpuLayers);

            _readyTcs.TrySetResult();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public LlmInitInfo GetDetailedStatus()
    {
        var models = _modelDiscovery.DiscoverModelsAsync().GetAwaiter().GetResult();

        return new LlmInitInfo
        {
            Status = _status,
            Progress = _progress,
            StatusMessage = _statusMessage,
            ElapsedTime = _stopwatch.Elapsed,
            SelectedGpu = _selectedGpu,
            SelectedModel = _selectedModel,
            GpuLayers = _config.GpuLayerCount,
            ErrorMessage = _errorMessage,
            AvailableModels = models.Select(m => m.FileName).ToList(),
            EngineStats = _engine.IsLoaded ? _engine.GetStats() : null
        };
    }

    private async Task WarmupModelAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Verify model is still loaded before warmup
            if (!_engine.IsLoaded)
            {
                _logger.LogWarning("Model not loaded, skipping warmup");
                return;
            }

            // Wait for any concurrent operations to settle
            await Task.Delay(100, cancellationToken);

            // Double-check model is still loaded
            if (!_engine.IsLoaded)
            {
                _logger.LogWarning("Model was unloaded during warmup delay, skipping");
                return;
            }

            // Generate a small response to warm up the model
            var warmupPrompt = "Hello";
            var response = await _engine.GenerateAsync(warmupPrompt, maxTokens: 10, cancellationToken: cancellationToken);
            _logger.LogDebug("Warmup response: {Response}", response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Warmup cancelled");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No model loaded"))
        {
            _logger.LogWarning("Model was unloaded during warmup, skipping");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup failed (non-fatal)");
        }
    }

    private void SetStatus(LlmInitStatus status, string message)
    {
        _status = status;
        _statusMessage = message;
        _logger.LogInformation("[LLM Init] {Status}: {Message}", status, message);
        OnStatusChanged?.Invoke(status, message);
    }

    private void SetProgress(int progress, string message)
    {
        _progress = Math.Clamp(progress, 0, 100);
        _statusMessage = message;
        OnProgressChanged?.Invoke(_progress, message);
    }

    public override void Dispose()
    {
        _initLock.Dispose();
        base.Dispose();
    }
}
