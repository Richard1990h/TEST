using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Threading;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// LLM inference engine using LLamaSharp with GPU support.
/// </summary>
public sealed class LlmEngine : ILlmEngine, IDisposable
{
    private readonly LlmConfig _config;
    private readonly ILogger<LlmEngine> _logger;
    private readonly IGpuDetector _gpuDetector;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private StatelessExecutor? _executor;

    private string? _currentModelPath;
    private volatile bool _isHealthy = true;
    private volatile bool _isBusy;

    // Statistics
    private long _totalTokensGenerated;
    private long _totalPromptTokens;
    private TimeSpan _totalGenerationTime;
    private int _activeStreams;

    public bool IsLoaded => _executor != null && _context != null && _weights != null;
    public bool IsHealthy => _isHealthy && IsLoaded;
    public string? CurrentModel => _currentModelPath != null ? Path.GetFileName(_currentModelPath) : null;
    public bool IsBusy => _isBusy;

    private static bool _nativeLibraryInitialized;
    private static readonly object _initLock = new();

    public LlmEngine(LlmConfig config, ILogger<LlmEngine> logger, IGpuDetector gpuDetector)
    {
        _config = config;
        _logger = logger;
        _gpuDetector = gpuDetector;

        // Initialize CUDA backend once
        InitializeNativeLibrary();
    }

    private void InitializeNativeLibrary()
    {
        // Native library is now initialized at application startup in Program.cs
        // This method is kept for backwards compatibility but does nothing
        if (_nativeLibraryInitialized) return;

        lock (_initLock)
        {
            if (_nativeLibraryInitialized) return;

            // Just log the status - actual initialization happens in Program.cs
            if (_gpuDetector.IsCudaAvailable)
            {
                _logger.LogInformation("CUDA backend configured for LLamaSharp");
            }
            else
            {
                _logger.LogInformation("CUDA not available, using CPU backend");
            }
            _nativeLibraryInitialized = true;
        }
    }

    private volatile bool _isLoading;

    public async Task LoadModelAsync(string? modelFile = null, CancellationToken cancellationToken = default)
    {
        // Prevent concurrent load attempts
        if (_isLoading)
        {
            _logger.LogWarning("Model load already in progress, waiting...");
            // Wait for existing load to complete
            while (_isLoading && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }
            if (IsLoaded)
            {
                _logger.LogInformation("Model was loaded by concurrent request");
                return;
            }
        }

        await _lock.WaitAsync(cancellationToken);
        _isLoading = true;
        try
        {
            // Double-check after acquiring lock
            if (IsLoaded && _currentModelPath == _config.ModelPath)
            {
                _logger.LogDebug("Model already loaded with same path, skipping reload");
                return;
            }

            // Unload existing model
            SafeDispose();

            // Determine model path - check ModelPath first, then fall back to directory/file
            string? modelPath;
            if (!string.IsNullOrEmpty(_config.ModelPath) && File.Exists(_config.ModelPath))
            {
                modelPath = _config.ModelPath;
            }
            else if (!string.IsNullOrEmpty(modelFile))
            {
                // Check if modelFile is a full path or just a filename
                if (Path.IsPathRooted(modelFile) && File.Exists(modelFile))
                {
                    modelPath = modelFile;
                }
                else
                {
                    var dir = _config.GetModelDirectoryPath();
                    modelPath = Path.Combine(dir, modelFile);
                }
            }
            else
            {
                modelPath = _config.GetModelFilePath();
            }

            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath ?? "No model specified"}");
            }

            _logger.LogInformation("Loading model: {ModelPath}", modelPath);

            var cudaAvailable = _gpuDetector.IsCudaAvailable;
            var gpuLayers = _config.GpuLayerCount;
            if (gpuLayers < 0)
            {
                gpuLayers = cudaAvailable ? 999 : 0;
            }
            else if (!cudaAvailable && gpuLayers > 0)
            {
                _logger.LogWarning("CUDA not available; forcing GPU layers to 0");
                gpuLayers = 0;
            }

            var threads = _config.Threads == 0
                ? (uint)Math.Max(1, Environment.ProcessorCount)
                : _config.Threads;

            var batchSize = _config.BatchSize == 0
                ? (cudaAvailable ? 512u : 128u)
                : _config.BatchSize;

            _config.GpuLayerCount = gpuLayers;
            _logger.LogInformation("GPU Layers: {GpuLayers}, Context Size: {ContextSize}",
                gpuLayers, _config.ContextSize);

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = _config.ContextSize,
                GpuLayerCount = gpuLayers,
                BatchSize = batchSize,
                Threads = (int)threads
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _context = _weights.CreateContext(parameters);
            // Use StatelessExecutor - we manage conversation history ourselves
            _executor = new StatelessExecutor(_weights, parameters);

            _currentModelPath = modelPath;
            _isHealthy = true;

            _logger.LogInformation("Model loaded successfully: {Model}", CurrentModel);
        }
        catch (Exception ex)
        {
            _isHealthy = false;
            _logger.LogError(ex, "Failed to load model");
            throw;
        }
        finally
        {
            _isLoading = false;
            _lock.Release();
        }
    }

    public Task UnloadModelAsync()
    {
        _lock.Wait();
        try
        {
            if (Volatile.Read(ref _activeStreams) > 0)
            {
                _logger.LogWarning("Unload requested while {ActiveStreams} streams are active", _activeStreams);
            }

            SafeDispose();
            _currentModelPath = null;
            _logger.LogInformation("Model unloaded");
        }
        finally
        {
            _lock.Release();
        }
        return Task.CompletedTask;
    }

    public async Task<string> GenerateAsync(
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        var result = new System.Text.StringBuilder();

        await foreach (var token in StreamAsync(prompt, maxTokens, temperature, cancellationToken))
        {
            result.Append(token);
        }

        return result.ToString().Trim();
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsLoaded)
        {
            throw new InvalidOperationException("No model loaded. Call LoadModelAsync first.");
        }

        var executor = _executor;
        if (executor == null)
        {
            throw new InvalidOperationException("Model executor not initialized. Ensure the model is loaded.");
        }

        Interlocked.Increment(ref _activeStreams);
        _isBusy = true;
        var stopwatch = Stopwatch.StartNew();
        var ttftStopwatch = Stopwatch.StartNew();
        int tokenCount = 0;
        var firstTokenLogged = false;

        try
        {
            // Build inference params
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens ?? _config.DefaultMaxTokens,
                AntiPrompts = _config.AntiPrompts.ToList(),
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = temperature ?? _config.Temperature,
                    TopP = _config.TopP,
                    TopK = _config.TopK,
                    RepeatPenalty = _config.RepetitionPenalty,
                    FrequencyPenalty = _config.FrequencyPenalty,
                    PresencePenalty = _config.PresencePenalty
                }
            };

            _totalPromptTokens += CountTokens(prompt);

            _logger.LogDebug("Starting generation: MaxTokens={MaxTokens}, Temp={Temp}",
                inferenceParams.MaxTokens, temperature ?? _config.Temperature);

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                tokenCount++;
                if (!firstTokenLogged)
                {
                    firstTokenLogged = true;
                    _logger.LogInformation("Local TTFT: {TtftMs}ms", ttftStopwatch.ElapsedMilliseconds);
                }

                if (tokenCount % 256 == 0)
                {
                    var elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                    _logger.LogDebug("Local tokens: {Tokens}, TPS: {TPS}",
                        tokenCount, tokenCount / elapsedSeconds);
                }
                yield return token;
            }

            stopwatch.Stop();
            _totalTokensGenerated += tokenCount;
            _totalGenerationTime += stopwatch.Elapsed;

            _logger.LogInformation("Local generation complete: {Tokens} tokens in {Time}ms ({TPS} tokens/sec)",
                tokenCount, stopwatch.ElapsedMilliseconds,
                tokenCount / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds));
        }
        finally
        {
            _isBusy = Interlocked.Decrement(ref _activeStreams) > 0;
        }
    }

    public int CountTokens(string text)
    {
        if (_context == null || string.IsNullOrEmpty(text))
            return 0;

        try
        {
            return _context.Tokenize(text).Length;
        }
        catch
        {
            // Fallback to rough estimate
            return text.Length / 4;
        }
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        var dir = _config.GetModelDirectoryPath();

        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        var extensions = new[] { "*.gguf", "*.bin", "*.safetensors" };
        return extensions
            .SelectMany(pattern => Directory.GetFiles(dir, pattern))
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n)
            .ToList()!;
    }

    public LlmEngineStats GetStats()
    {
        var modelSize = 0L;
        if (!string.IsNullOrEmpty(_currentModelPath) && File.Exists(_currentModelPath))
        {
            modelSize = new FileInfo(_currentModelPath).Length;
        }

        var totalSeconds = _totalGenerationTime.TotalSeconds;
        var avgTps = totalSeconds > 0 ? _totalTokensGenerated / totalSeconds : 0;

        return new LlmEngineStats
        {
            ModelName = CurrentModel,
            ModelSizeBytes = modelSize,
            ContextSize = _config.ContextSize,
            GpuLayers = _config.GpuLayerCount,
            TotalTokensGenerated = _totalTokensGenerated,
            TotalPromptTokens = _totalPromptTokens,
            TotalGenerationTime = _totalGenerationTime,
            AverageTokensPerSecond = avgTps
        };
    }

    private void SafeDispose()
    {
        try { (_executor as IDisposable)?.Dispose(); } catch { }
        try { _context?.Dispose(); } catch { }
        try { _weights?.Dispose(); } catch { }

        _executor = null;
        _context = null;
        _weights = null;
    }

    public void Dispose()
    {
        SafeDispose();
        _lock.Dispose();
    }
}
