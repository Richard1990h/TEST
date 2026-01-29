using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// GPU detection and capability assessment for optimal LLM configuration.
/// </summary>
public interface IGpuDetector
{
    /// <summary>
    /// Detect all available GPUs and their capabilities.
    /// </summary>
    Task<GpuInfo[]> DetectGpusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the best GPU for LLM inference.
    /// </summary>
    Task<GpuInfo?> GetBestGpuAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate optimal GPU layers based on model size and available VRAM.
    /// </summary>
    int CalculateOptimalGpuLayers(long modelSizeBytes, long availableVram, int totalLayers = 32);

    /// <summary>
    /// Check if CUDA is available.
    /// </summary>
    bool IsCudaAvailable { get; }

    /// <summary>
    /// Check if Vulkan is available.
    /// </summary>
    bool IsVulkanAvailable { get; }
}

/// <summary>
/// Information about a GPU device.
/// </summary>
public class GpuInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public long TotalMemoryBytes { get; set; }
    public long FreeMemoryBytes { get; set; }
    public long UsedMemoryBytes => TotalMemoryBytes - FreeMemoryBytes;
    public double MemoryUsagePercent => TotalMemoryBytes > 0 ? (UsedMemoryBytes * 100.0 / TotalMemoryBytes) : 0;
    public bool SupportsCuda { get; set; }
    public bool SupportsVulkan { get; set; }
    public int CudaComputeCapabilityMajor { get; set; }
    public int CudaComputeCapabilityMinor { get; set; }
    public string CudaComputeCapability => $"{CudaComputeCapabilityMajor}.{CudaComputeCapabilityMinor}";
    public bool IsRecommended { get; set; }
    public string RecommendationReason { get; set; } = "";

    // Performance estimates
    public int EstimatedMaxLayers { get; set; }
    public double EstimatedTokensPerSecond { get; set; }

    public string TotalMemoryFormatted => FormatBytes(TotalMemoryBytes);
    public string FreeMemoryFormatted => FormatBytes(FreeMemoryBytes);

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
/// GPU detector implementation for Windows.
/// </summary>
public class GpuDetector : IGpuDetector
{
    private readonly ILogger<GpuDetector> _logger;
    private bool? _cudaAvailable;
    private bool? _vulkanAvailable;
    private GpuInfo[]? _cachedGpus;
    private DateTime _lastDetection = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public GpuDetector(ILogger<GpuDetector> logger)
    {
        _logger = logger;
    }

    public bool IsCudaAvailable
    {
        get
        {
            if (_cudaAvailable == null)
            {
                _cudaAvailable = CheckCudaAvailability();
            }
            return _cudaAvailable.Value;
        }
    }

    public bool IsVulkanAvailable
    {
        get
        {
            if (_vulkanAvailable == null)
            {
                _vulkanAvailable = CheckVulkanAvailability();
            }
            return _vulkanAvailable.Value;
        }
    }

    public async Task<GpuInfo[]> DetectGpusAsync(CancellationToken cancellationToken = default)
    {
        // Use cached results if recent
        if (_cachedGpus != null && DateTime.UtcNow - _lastDetection < _cacheExpiry)
        {
            return _cachedGpus;
        }

        var gpus = new List<GpuInfo>();

        try
        {
            _logger.LogInformation("Detecting GPUs...");

            // Try nvidia-smi first for NVIDIA GPUs
            var nvidiaGpus = await DetectNvidiaGpusAsync(cancellationToken);
            gpus.AddRange(nvidiaGpus);

            // Fall back to WMI for other GPUs
            if (!gpus.Any())
            {
                var wmiGpus = DetectGpusViaWmi();
                gpus.AddRange(wmiGpus);
            }

            // Assess recommendations
            foreach (var gpu in gpus)
            {
                AssessGpuRecommendation(gpu);
            }

            _cachedGpus = gpus.ToArray();
            _lastDetection = DateTime.UtcNow;

            _logger.LogInformation("Detected {Count} GPU(s)", gpus.Count);
            foreach (var gpu in gpus)
            {
                _logger.LogInformation("  GPU {Index}: {Name} - {Memory} VRAM ({Free} free), CUDA: {Cuda}",
                    gpu.Index, gpu.Name, gpu.TotalMemoryFormatted, gpu.FreeMemoryFormatted, gpu.SupportsCuda);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU detection failed, will use CPU");
            _cachedGpus = Array.Empty<GpuInfo>();
        }

        return _cachedGpus;
    }

    public async Task<GpuInfo?> GetBestGpuAsync(CancellationToken cancellationToken = default)
    {
        var gpus = await DetectGpusAsync(cancellationToken);

        // Prefer CUDA GPUs with most free memory
        return gpus
            .Where(g => g.SupportsCuda)
            .OrderByDescending(g => g.FreeMemoryBytes)
            .ThenByDescending(g => g.CudaComputeCapabilityMajor)
            .FirstOrDefault()
            ?? gpus
                .OrderByDescending(g => g.FreeMemoryBytes)
                .FirstOrDefault();
    }

    public int CalculateOptimalGpuLayers(long modelSizeBytes, long availableVram, int totalLayers = 32)
    {
        if (availableVram <= 0 || modelSizeBytes <= 0)
            return 0;

        // Reserve 500MB for context and operations
        const long reservedVram = 500 * 1024 * 1024;
        var usableVram = Math.Max(0, availableVram - reservedVram);

        // Estimate memory per layer (rough approximation)
        var memoryPerLayer = modelSizeBytes / totalLayers;

        // Calculate how many layers fit
        var possibleLayers = (int)(usableVram / memoryPerLayer);

        // Clamp to valid range, prefer all layers if possible
        var optimalLayers = Math.Clamp(possibleLayers, 0, totalLayers);

        _logger.LogDebug(
            "GPU layer calculation: Model={ModelMB}MB, VRAM={VramMB}MB, PerLayer={LayerMB}MB -> {Layers} layers",
            modelSizeBytes / 1024 / 1024,
            availableVram / 1024 / 1024,
            memoryPerLayer / 1024 / 1024,
            optimalLayers);

        return optimalLayers;
    }

    private async Task<List<GpuInfo>> DetectNvidiaGpusAsync(CancellationToken cancellationToken)
    {
        var gpus = new List<GpuInfo>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=index,name,memory.total,memory.free,compute_cap --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return gpus;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0) return gpus;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length >= 5)
                {
                    var computeCap = parts[4].Split('.');
                    gpus.Add(new GpuInfo
                    {
                        Index = int.Parse(parts[0]),
                        Name = parts[1],
                        Vendor = "NVIDIA",
                        TotalMemoryBytes = long.Parse(parts[2]) * 1024 * 1024, // nvidia-smi reports in MiB
                        FreeMemoryBytes = long.Parse(parts[3]) * 1024 * 1024,
                        SupportsCuda = true,
                        SupportsVulkan = true,
                        CudaComputeCapabilityMajor = int.Parse(computeCap[0]),
                        CudaComputeCapabilityMinor = computeCap.Length > 1 ? int.Parse(computeCap[1]) : 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "nvidia-smi not available or failed");
        }

        return gpus;
    }

    private List<GpuInfo> DetectGpusViaWmi()
    {
        var gpus = new List<GpuInfo>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return gpus;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            int index = 0;

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                var adapterRam = Convert.ToInt64(obj["AdapterRAM"] ?? 0);

                // WMI sometimes returns 0 or 4GB max for AdapterRAM
                if (adapterRam <= 0)
                    adapterRam = 4L * 1024 * 1024 * 1024; // Assume 4GB

                var gpu = new GpuInfo
                {
                    Index = index++,
                    Name = name,
                    Vendor = obj["AdapterCompatibility"]?.ToString() ?? "Unknown",
                    TotalMemoryBytes = adapterRam,
                    FreeMemoryBytes = adapterRam, // WMI doesn't provide free memory
                    SupportsCuda = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("GTX", StringComparison.OrdinalIgnoreCase),
                    SupportsVulkan = !name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                                     name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
                };

                // Skip integrated graphics if dedicated GPU exists
                if (name.Contains("Intel") && !name.Contains("Arc"))
                {
                    gpu.IsRecommended = false;
                    gpu.RecommendationReason = "Integrated graphics - prefer dedicated GPU";
                }

                gpus.Add(gpu);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI GPU detection failed");
        }

        return gpus;
    }

    private void AssessGpuRecommendation(GpuInfo gpu)
    {
        // Minimum 4GB VRAM for decent LLM inference
        const long minVram = 4L * 1024 * 1024 * 1024;
        // Recommended 8GB+ for good performance
        const long recommendedVram = 8L * 1024 * 1024 * 1024;

        if (!gpu.SupportsCuda)
        {
            gpu.IsRecommended = false;
            gpu.RecommendationReason = "CUDA not supported - will use CPU fallback";
            gpu.EstimatedTokensPerSecond = 5; // CPU estimate
            return;
        }

        if (gpu.TotalMemoryBytes < minVram)
        {
            gpu.IsRecommended = false;
            gpu.RecommendationReason = $"Insufficient VRAM ({gpu.TotalMemoryFormatted}) - minimum 4GB recommended";
            gpu.EstimatedTokensPerSecond = 10;
            return;
        }

        if (gpu.CudaComputeCapabilityMajor < 6)
        {
            gpu.IsRecommended = false;
            gpu.RecommendationReason = $"Compute capability {gpu.CudaComputeCapability} too old - requires 6.0+";
            gpu.EstimatedTokensPerSecond = 15;
            return;
        }

        gpu.IsRecommended = true;

        if (gpu.TotalMemoryBytes >= recommendedVram)
        {
            gpu.RecommendationReason = "Excellent for LLM inference";
            gpu.EstimatedTokensPerSecond = gpu.TotalMemoryBytes >= 12L * 1024 * 1024 * 1024 ? 50 : 35;
        }
        else
        {
            gpu.RecommendationReason = "Good for smaller models";
            gpu.EstimatedTokensPerSecond = 25;
        }

        // Estimate max layers based on VRAM (assuming ~200MB per layer for 7B model)
        gpu.EstimatedMaxLayers = (int)(gpu.FreeMemoryBytes / (200L * 1024 * 1024));
    }

    private bool CheckCudaAvailability()
    {
        try
        {
            // Check for CUDA DLL
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
                if (!string.IsNullOrEmpty(cudaPath))
                {
                    _logger.LogDebug("CUDA_PATH found: {Path}", cudaPath);
                    return true;
                }

                // Check common CUDA locations
                var commonPaths = new[]
                {
                    @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA",
                    @"C:\Program Files\NVIDIA Corporation\CUDA"
                };

                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                    {
                        _logger.LogDebug("CUDA found at: {Path}", path);
                        return true;
                    }
                }
            }

            // Try running nvidia-smi
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
        }
        catch
        {
            // CUDA not available
        }

        return false;
    }

    private bool CheckVulkanAvailability()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Check for Vulkan runtime
                var vulkanPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll");
                return File.Exists(vulkanPath);
            }
        }
        catch
        {
            // Vulkan not available
        }

        return false;
    }
}
