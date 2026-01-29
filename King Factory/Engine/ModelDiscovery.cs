using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Discovers and validates GGUF model files.
/// </summary>
public interface IModelDiscovery
{
    /// <summary>
    /// Scan for available models in configured directories.
    /// </summary>
    Task<ModelInfo[]> DiscoverModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get info about a specific model file.
    /// </summary>
    Task<ModelInfo?> GetModelInfoAsync(string modelPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the best model based on available hardware.
    /// </summary>
    Task<ModelInfo?> FindBestModelAsync(long availableVram, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a model file is usable.
    /// </summary>
    Task<ModelValidationResult> ValidateModelAsync(string modelPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a model file.
/// </summary>
public class ModelInfo
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SizeFormatted => FormatBytes(SizeBytes);
    public DateTime LastModified { get; set; }

    // GGUF Metadata
    public string Architecture { get; set; } = "unknown";
    public long ParameterCount { get; set; }
    public string ParameterCountFormatted => FormatParameterCount(ParameterCount);
    public int ContextLength { get; set; }
    public string Quantization { get; set; } = "unknown";
    public int LayerCount { get; set; }
    public int EmbeddingLength { get; set; }
    public int HeadCount { get; set; }

    // Computed
    public bool IsValid { get; set; }
    public string ValidationMessage { get; set; } = "";
    public long EstimatedVramBytes { get; set; }
    public string EstimatedVramFormatted => FormatBytes(EstimatedVramBytes);
    public ModelSize ModelSize { get; set; }
    public int RecommendedGpuLayers { get; set; }
    public double EstimatedTokensPerSecond { get; set; }

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

    private static string FormatParameterCount(long count)
    {
        if (count >= 1_000_000_000)
            return $"{count / 1_000_000_000.0:0.#}B";
        if (count >= 1_000_000)
            return $"{count / 1_000_000.0:0.#}M";
        if (count >= 1_000)
            return $"{count / 1_000.0:0.#}K";
        return count.ToString();
    }
}

/// <summary>
/// Model size category.
/// </summary>
public enum ModelSize
{
    Unknown,
    Tiny,      // < 1B params
    Small,     // 1-3B params
    Medium,    // 3-7B params
    Large,     // 7-13B params
    XLarge,    // 13-30B params
    Huge       // 30B+ params
}

/// <summary>
/// Result of model validation.
/// </summary>
public class ModelValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = "";
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public ModelInfo? ModelInfo { get; set; }
}

/// <summary>
/// Model discovery implementation.
/// </summary>
public class ModelDiscovery : IModelDiscovery
{
    private readonly ILogger<ModelDiscovery> _logger;
    private readonly LlmConfig _config;
    private readonly IGpuDetector _gpuDetector;

    // GGUF Magic number
    private static readonly byte[] GgufMagic = { 0x47, 0x47, 0x55, 0x46 }; // "GGUF"

    public ModelDiscovery(
        ILogger<ModelDiscovery> logger,
        LlmConfig config,
        IGpuDetector gpuDetector)
    {
        _logger = logger;
        _config = config;
        _gpuDetector = gpuDetector;
    }

    public async Task<ModelInfo[]> DiscoverModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = new List<ModelInfo>();
        var searchPaths = GetSearchPaths();

        _logger.LogInformation("Scanning for models in {Count} locations...", searchPaths.Count);

        foreach (var searchPath in searchPaths)
        {
            if (!Directory.Exists(searchPath))
            {
                _logger.LogDebug("Directory not found: {Path}", searchPath);
                continue;
            }

            try
            {
                var files = Directory.GetFiles(searchPath, "*.gguf", SearchOption.AllDirectories);
                _logger.LogDebug("Found {Count} .gguf files in {Path}", files.Length, searchPath);

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var info = await GetModelInfoAsync(file, cancellationToken);
                    if (info != null)
                    {
                        models.Add(info);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning {Path}", searchPath);
            }
        }

        // Sort by size (prefer smaller models that fit in VRAM)
        var sorted = models.OrderBy(m => m.SizeBytes).ToArray();

        _logger.LogInformation("Discovered {Count} valid models", sorted.Length);
        foreach (var model in sorted)
        {
            _logger.LogDebug("  {Name}: {Size}, {Params} params, {Quant}",
                model.FileName, model.SizeFormatted, model.ParameterCountFormatted, model.Quantization);
        }

        return sorted;
    }

    public async Task<ModelInfo?> GetModelInfoAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modelPath))
            return null;

        var fileInfo = new FileInfo(modelPath);
        var info = new ModelInfo
        {
            Path = modelPath,
            FileName = fileInfo.Name,
            Name = Path.GetFileNameWithoutExtension(fileInfo.Name),
            SizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };

        try
        {
            // Read GGUF header and metadata
            await using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using var reader = new BinaryReader(stream);

            // Check magic number
            var magic = reader.ReadBytes(4);
            if (!magic.SequenceEqual(GgufMagic))
            {
                info.IsValid = false;
                info.ValidationMessage = "Invalid GGUF file (bad magic number)";
                return info;
            }

            // Read version
            var version = reader.ReadUInt32();
            if (version < 2 || version > 3)
            {
                _logger.LogWarning("Unknown GGUF version {Version} for {Path}", version, modelPath);
            }

            // Read tensor count and metadata count
            var tensorCount = reader.ReadUInt64();
            var metadataCount = reader.ReadUInt64();

            _logger.LogDebug("GGUF v{Version}: {Tensors} tensors, {Metadata} metadata entries",
                version, tensorCount, metadataCount);

            // Parse metadata
            var metadata = await ReadGgufMetadataAsync(reader, (int)metadataCount, cancellationToken);

            // Extract key metadata
            info.Architecture = GetMetadataString(metadata, "general.architecture") ?? "unknown";
            info.ContextLength = GetMetadataInt(metadata, $"{info.Architecture}.context_length", 4096);
            info.LayerCount = GetMetadataInt(metadata, $"{info.Architecture}.block_count", 32);
            info.EmbeddingLength = GetMetadataInt(metadata, $"{info.Architecture}.embedding_length", 4096);
            info.HeadCount = GetMetadataInt(metadata, $"{info.Architecture}.attention.head_count", 32);

            // Try to get quantization from filename or metadata
            info.Quantization = DetectQuantization(info.FileName, metadata);

            // Estimate parameter count from model structure
            info.ParameterCount = EstimateParameterCount(info);

            // Classify model size
            info.ModelSize = ClassifyModelSize(info.ParameterCount);

            // Estimate VRAM requirements
            info.EstimatedVramBytes = EstimateVramRequirement(info);

            // Calculate recommended GPU layers based on available VRAM
            var bestGpu = await _gpuDetector.GetBestGpuAsync(cancellationToken);
            if (bestGpu != null)
            {
                info.RecommendedGpuLayers = _gpuDetector.CalculateOptimalGpuLayers(
                    info.SizeBytes,
                    bestGpu.FreeMemoryBytes,
                    info.LayerCount);

                // Estimate tokens/sec based on GPU and model
                info.EstimatedTokensPerSecond = EstimateTokensPerSecond(info, bestGpu);
            }

            info.IsValid = true;
            info.ValidationMessage = "OK";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read model info for {Path}", modelPath);
            info.IsValid = false;
            info.ValidationMessage = $"Error reading model: {ex.Message}";
        }

        return info;
    }

    public async Task<ModelInfo?> FindBestModelAsync(long availableVram, CancellationToken cancellationToken = default)
    {
        var models = await DiscoverModelsAsync(cancellationToken);
        if (!models.Any())
            return null;

        // Find largest model that fits in VRAM with some headroom
        var headroom = 1024L * 1024 * 1024; // 1GB headroom
        var targetVram = availableVram - headroom;

        var bestModel = models
            .Where(m => m.IsValid && m.EstimatedVramBytes <= targetVram)
            .OrderByDescending(m => m.ParameterCount) // Prefer larger models
            .ThenBy(m => m.Quantization.Contains("Q4")) // Prefer higher quality quantization
            .FirstOrDefault();

        if (bestModel != null)
        {
            _logger.LogInformation("Selected best model: {Name} ({Size}, {Vram} VRAM)",
                bestModel.FileName, bestModel.SizeFormatted, bestModel.EstimatedVramFormatted);
            return bestModel;
        }

        // Fall back to smallest model if nothing fits
        bestModel = models.Where(m => m.IsValid).OrderBy(m => m.SizeBytes).FirstOrDefault();
        if (bestModel != null)
        {
            _logger.LogWarning("No model fits in VRAM ({VramMB}MB), using smallest: {Name}",
                availableVram / 1024 / 1024, bestModel.FileName);
        }

        return bestModel;
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        var result = new ModelValidationResult();

        if (!File.Exists(modelPath))
        {
            result.IsValid = false;
            result.Message = "File not found";
            result.Errors.Add($"Model file does not exist: {modelPath}");
            return result;
        }

        var info = await GetModelInfoAsync(modelPath, cancellationToken);
        result.ModelInfo = info;

        if (info == null)
        {
            result.IsValid = false;
            result.Message = "Failed to read model";
            result.Errors.Add("Could not read model file");
            return result;
        }

        if (!info.IsValid)
        {
            result.IsValid = false;
            result.Message = info.ValidationMessage;
            result.Errors.Add(info.ValidationMessage);
            return result;
        }

        // Additional validations
        if (info.SizeBytes < 100 * 1024 * 1024) // Less than 100MB
        {
            result.Warnings.Add("Model is very small - may have limited capabilities");
        }

        if (info.ContextLength < 2048)
        {
            result.Warnings.Add($"Context length ({info.ContextLength}) is small - may limit conversation length");
        }

        var bestGpu = await _gpuDetector.GetBestGpuAsync(cancellationToken);
        if (bestGpu != null && info.EstimatedVramBytes > bestGpu.FreeMemoryBytes)
        {
            result.Warnings.Add($"Model may not fully fit in GPU VRAM ({info.EstimatedVramFormatted} needed, {bestGpu.FreeMemoryFormatted} available)");
        }

        result.IsValid = true;
        result.Message = "Model is valid and ready to use";

        return result;
    }

    private List<string> GetSearchPaths()
    {
        var paths = new List<string>();

        // Application's LLM folder (highest priority)
        var appLlmDir = Path.Combine(AppContext.BaseDirectory, "LLM");
        if (!string.IsNullOrEmpty(appLlmDir))
            paths.Add(appLlmDir);

        // Configured model directory
        var configDir = _config.GetModelDirectoryPath();
        if (!string.IsNullOrEmpty(configDir) && !paths.Contains(configDir))
            paths.Add(configDir);

        // Explicit model path directory
        if (!string.IsNullOrEmpty(_config.ModelPath))
        {
            var dir = Path.GetDirectoryName(_config.ModelPath);
            if (!string.IsNullOrEmpty(dir) && !paths.Contains(dir))
                paths.Add(dir);
        }

        // Common model locations
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        paths.Add(Path.Combine(localAppData, "LittleHelperAI", "Models"));
        paths.Add(Path.Combine(localAppData, "nomic.ai", "GPT4All"));
        paths.Add(Path.Combine(localAppData, "lm-studio", "models"));

        // User's home directory
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        paths.Add(Path.Combine(userProfile, ".cache", "lm-studio", "models"));
        paths.Add(Path.Combine(userProfile, "models"));

        return paths.Distinct().ToList();
    }

    private Task<Dictionary<string, object>> ReadGgufMetadataAsync(
        BinaryReader reader,
        int count,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>();

        for (int i = 0; i < count && i < 1000; i++) // Limit to prevent issues
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Read key
                var keyLength = reader.ReadUInt64();
                if (keyLength > 1024) break; // Safety check
                var keyBytes = reader.ReadBytes((int)keyLength);
                var key = Encoding.UTF8.GetString(keyBytes);

                // Read value type
                var valueType = reader.ReadUInt32();

                // Read value based on type
                object? value = valueType switch
                {
                    0 => reader.ReadByte(),      // UINT8
                    1 => reader.ReadSByte(),     // INT8
                    2 => reader.ReadUInt16(),    // UINT16
                    3 => reader.ReadInt16(),     // INT16
                    4 => reader.ReadUInt32(),    // UINT32
                    5 => reader.ReadInt32(),     // INT32
                    6 => reader.ReadSingle(),    // FLOAT32
                    7 => reader.ReadBoolean(),   // BOOL
                    8 => ReadGgufString(reader), // STRING
                    9 => ReadGgufArray(reader),  // ARRAY
                    10 => reader.ReadUInt64(),   // UINT64
                    11 => reader.ReadInt64(),    // INT64
                    12 => reader.ReadDouble(),   // FLOAT64
                    _ => null
                };

                if (value != null)
                {
                    metadata[key] = value;
                }
            }
            catch
            {
                break; // Stop on any read error
            }
        }

        return Task.FromResult(metadata);
    }

    private string ReadGgufString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        if (length > 65536) return ""; // Safety check
        var bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private object[] ReadGgufArray(BinaryReader reader)
    {
        var elementType = reader.ReadUInt32();
        var count = reader.ReadUInt64();
        if (count > 10000) return Array.Empty<object>(); // Safety check

        var array = new object[count];
        for (ulong i = 0; i < count; i++)
        {
            array[i] = elementType switch
            {
                0 => reader.ReadByte(),
                1 => reader.ReadSByte(),
                2 => reader.ReadUInt16(),
                3 => reader.ReadInt16(),
                4 => reader.ReadUInt32(),
                5 => reader.ReadInt32(),
                6 => reader.ReadSingle(),
                7 => reader.ReadBoolean(),
                8 => ReadGgufString(reader),
                10 => reader.ReadUInt64(),
                11 => reader.ReadInt64(),
                12 => reader.ReadDouble(),
                _ => 0
            };
        }
        return array;
    }

    private string? GetMetadataString(Dictionary<string, object> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) ? value.ToString() : null;
    }

    private int GetMetadataInt(Dictionary<string, object> metadata, string key, int defaultValue = 0)
    {
        if (metadata.TryGetValue(key, out var value))
        {
            return Convert.ToInt32(value);
        }
        return defaultValue;
    }

    private string DetectQuantization(string filename, Dictionary<string, object> metadata)
    {
        // Try to get from metadata
        if (metadata.TryGetValue("general.quantization_version", out var quantVer))
        {
            return $"Q{quantVer}";
        }

        // Parse from filename
        var upperName = filename.ToUpperInvariant();
        var quantPatterns = new[]
        {
            "Q8_0", "Q6_K", "Q5_K_M", "Q5_K_S", "Q5_1", "Q5_0",
            "Q4_K_M", "Q4_K_S", "Q4_1", "Q4_0", "Q3_K_M", "Q3_K_S",
            "Q2_K", "IQ4_XS", "IQ4_NL", "IQ3_XXS", "IQ2_XXS",
            "F16", "F32", "BF16"
        };

        foreach (var pattern in quantPatterns)
        {
            if (upperName.Contains(pattern.Replace("_", "")) || upperName.Contains(pattern))
            {
                return pattern;
            }
        }

        return "unknown";
    }

    private long EstimateParameterCount(ModelInfo info)
    {
        // Rough estimation based on model dimensions
        // Parameters ~= 12 * layers * embedding^2 (for typical transformer)
        var embedding = (long)info.EmbeddingLength;
        var layers = (long)info.LayerCount;

        return 12 * layers * embedding * embedding / 1_000_000 * 1_000_000; // Round to millions
    }

    private ModelSize ClassifyModelSize(long parameterCount)
    {
        return parameterCount switch
        {
            < 1_000_000_000 => ModelSize.Tiny,
            < 3_000_000_000 => ModelSize.Small,
            < 7_000_000_000 => ModelSize.Medium,
            < 13_000_000_000 => ModelSize.Large,
            < 30_000_000_000 => ModelSize.XLarge,
            _ => ModelSize.Huge
        };
    }

    private long EstimateVramRequirement(ModelInfo info)
    {
        // Base requirement is roughly the file size
        // Plus context buffer: context_length * embedding_length * 2 (K+V) * 2 (bytes for fp16) * layers
        var contextBuffer = (long)info.ContextLength * info.EmbeddingLength * 2 * 2 * info.LayerCount;

        // Add 20% overhead for activations and workspace
        return (long)(info.SizeBytes * 1.2) + contextBuffer;
    }

    private double EstimateTokensPerSecond(ModelInfo model, GpuInfo gpu)
    {
        // Rough estimates based on model size and GPU
        var baseSpeed = gpu.EstimatedTokensPerSecond;

        // Adjust for model size
        var sizeMultiplier = model.ModelSize switch
        {
            ModelSize.Tiny => 2.0,
            ModelSize.Small => 1.5,
            ModelSize.Medium => 1.0,
            ModelSize.Large => 0.7,
            ModelSize.XLarge => 0.4,
            ModelSize.Huge => 0.2,
            _ => 1.0
        };

        // Adjust for quantization (lower quantization = faster)
        var quantMultiplier = model.Quantization switch
        {
            "Q2_K" or "IQ2_XXS" => 1.4,
            "Q3_K_S" or "Q3_K_M" or "IQ3_XXS" => 1.3,
            "Q4_0" or "Q4_1" or "Q4_K_S" or "Q4_K_M" or "IQ4_XS" or "IQ4_NL" => 1.2,
            "Q5_0" or "Q5_1" or "Q5_K_S" or "Q5_K_M" => 1.1,
            "Q6_K" => 1.0,
            "Q8_0" => 0.9,
            "F16" or "BF16" => 0.7,
            "F32" => 0.4,
            _ => 1.0
        };

        return baseSpeed * sizeMultiplier * quantMultiplier;
    }
}
