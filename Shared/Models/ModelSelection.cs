using System.ComponentModel.DataAnnotations;

namespace LittleHelperAI.Shared.Models
{
    /// <summary>
    /// Available LLM providers.
    /// </summary>
    public enum LlmProvider
    {
        Local = 0,
        Ollama = 1
    }

    /// <summary>
    /// Request to select a model.
    /// </summary>
    public class ModelSelectionRequest
    {
        [Required]
        public string ModelName { get; set; } = string.Empty;

        public LlmProvider Provider { get; set; } = LlmProvider.Local;
    }

    /// <summary>
    /// Response containing current model info and available models.
    /// </summary>
    public class ModelInfoResponse
    {
        public string CurrentModel { get; set; } = string.Empty;
        public LlmProvider CurrentProvider { get; set; } = LlmProvider.Local;
        public List<string> AvailableModels { get; set; } = new List<string>();
        public string Metadata { get; set; } = string.Empty;
    }

    /// <summary>
    /// Information about an LLM provider.
    /// </summary>
    public class ProviderInfo
    {
        public LlmProvider Provider { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public List<ModelInfo> Models { get; set; } = new List<ModelInfo>();
    }

    /// <summary>
    /// Information about a specific model.
    /// </summary>
    public class ModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public LlmProvider Provider { get; set; }
        public long? SizeBytes { get; set; }
        public string? ParameterSize { get; set; }
    }

    /// <summary>
    /// Response containing all available providers and their models.
    /// </summary>
    public class ProvidersResponse
    {
        public LlmProvider CurrentProvider { get; set; } = LlmProvider.Local;
        public string? CurrentModel { get; set; }
        public List<ProviderInfo> Providers { get; set; } = new List<ProviderInfo>();
    }
}
