namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Configuration for the LLM engine.
/// </summary>
public class LlmConfig
{
    /// <summary>
    /// Path to the directory containing GGUF model files.
    /// </summary>
    public string ModelDirectory { get; set; } = "LLM";

    /// <summary>
    /// Specific model file to load. If null, loads first .gguf file found.
    /// </summary>
    public string? ModelFile { get; set; }

    /// <summary>
    /// Full path to the model file. Takes precedence over ModelDirectory/ModelFile.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Context window size in tokens.
    /// </summary>
    public uint ContextSize { get; set; } = 4096;

    /// <summary>
    /// Number of layers to offload to GPU. Set to -1 for all layers.
    /// </summary>
    public int GpuLayerCount { get; set; } = -1;

    /// <summary>
    /// Batch size for prompt processing.
    /// </summary>
    public uint BatchSize { get; set; } = 0; // 0 = auto-detect

    /// <summary>
    /// Number of threads for CPU inference.
    /// </summary>
    public uint Threads { get; set; } = 0; // 0 = auto-detect

    /// <summary>
    /// Default maximum tokens to generate.
    /// </summary>
    public int DefaultMaxTokens { get; set; } = 2048;

    /// <summary>
    /// Temperature for sampling (0.0 = deterministic, 1.0 = creative).
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Top-p (nucleus) sampling threshold.
    /// </summary>
    public float TopP { get; set; } = 0.9f;

    /// <summary>
    /// Top-k sampling (0 = disabled).
    /// </summary>
    public int TopK { get; set; } = 40;

    /// <summary>
    /// Repetition penalty (1.0 = no penalty).
    /// </summary>
    public float RepetitionPenalty { get; set; } = 1.1f;

    /// <summary>
    /// Frequency penalty (0.0 = no penalty).
    /// </summary>
    public float FrequencyPenalty { get; set; } = 0.0f;

    /// <summary>
    /// Presence penalty (0.0 = no penalty).
    /// </summary>
    public float PresencePenalty { get; set; } = 0.0f;

    /// <summary>
    /// Enable Mirostat sampling (0 = disabled, 1 = Mirostat, 2 = Mirostat 2.0).
    /// </summary>
    public int MirostatMode { get; set; } = 0;

    /// <summary>
    /// Mirostat target entropy (tau).
    /// </summary>
    public float MirostatTau { get; set; } = 5.0f;

    /// <summary>
    /// Mirostat learning rate (eta).
    /// </summary>
    public float MirostatEta { get; set; } = 0.1f;

    /// <summary>
    /// Enable flash attention for faster inference.
    /// </summary>
    public bool UseFlashAttention { get; set; } = true;

    /// <summary>
    /// Seed for reproducible outputs. -1 for random.
    /// </summary>
    public int Seed { get; set; } = -1;

    /// <summary>
    /// Anti-prompts that stop generation.
    /// </summary>
    public string[] AntiPrompts { get; set; } = new[]
    {
        "<|im_end|>",       // Qwen/ChatML stop token
        "<|im_start|>",     // Prevent continuing into next turn
        "<|endoftext|>",    // Qwen end token
        "User:",
        "Human:",
        "[/INST]",
        "</s>",
        "<|eot_id|>",
        "<|end|>"
    };

    /// <summary>
    /// Enable Pipeline V2 (declarative pipeline engine).
    /// When enabled and a V2 pipeline is configured, it will be used instead of the legacy system.
    /// </summary>
    public bool UsePipelineV2 { get; set; } = false;

    /// <summary>
    /// Gets the full path to the model directory.
    /// </summary>
    public string GetModelDirectoryPath()
    {
        if (Path.IsPathRooted(ModelDirectory))
            return ModelDirectory;

        return Path.Combine(AppContext.BaseDirectory, ModelDirectory);
    }

    /// <summary>
    /// Gets the full path to the model file.
    /// </summary>
    public string? GetModelFilePath()
    {
        var dir = GetModelDirectoryPath();

        if (!Directory.Exists(dir))
            return null;

        if (!string.IsNullOrEmpty(ModelFile))
        {
            var path = Path.Combine(dir, ModelFile);
            return File.Exists(path) ? path : null;
        }

        var extensions = new[] { "*.gguf", "*.bin", "*.safetensors" };
        var files = extensions
            .SelectMany(pattern => Directory.GetFiles(dir, pattern))
            .OrderBy(f => f)
            .ToList();

        return files.FirstOrDefault();
    }
}
