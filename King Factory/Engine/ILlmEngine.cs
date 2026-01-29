namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Interface for LLM inference engine.
/// </summary>
public interface ILlmEngine
{
    /// <summary>
    /// Whether the model is currently loaded and ready.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Whether the engine is healthy and operational.
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Currently loaded model name.
    /// </summary>
    string? CurrentModel { get; }

    /// <summary>
    /// Whether the engine is currently generating.
    /// </summary>
    bool IsBusy { get; }

    /// <summary>
    /// Load a model from the configured directory.
    /// </summary>
    Task LoadModelAsync(string? modelFile = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unload the current model to free memory.
    /// </summary>
    Task UnloadModelAsync();

    /// <summary>
    /// Generate a complete response (non-streaming).
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a streaming response.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Count tokens in a string.
    /// </summary>
    int CountTokens(string text);

    /// <summary>
    /// Get available models in the model directory.
    /// </summary>
    IReadOnlyList<string> GetAvailableModels();

    /// <summary>
    /// Get engine statistics.
    /// </summary>
    LlmEngineStats GetStats();
}

/// <summary>
/// Statistics about the LLM engine.
/// </summary>
public class LlmEngineStats
{
    public string? ModelName { get; set; }
    public long ModelSizeBytes { get; set; }
    public uint ContextSize { get; set; }
    public int GpuLayers { get; set; }
    public long TotalTokensGenerated { get; set; }
    public long TotalPromptTokens { get; set; }
    public TimeSpan TotalGenerationTime { get; set; }
    public double AverageTokensPerSecond { get; set; }
}
