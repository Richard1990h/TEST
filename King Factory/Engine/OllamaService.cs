using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Service for interacting with Ollama API.
/// </summary>
public interface IOllamaService
{
    /// <summary>
    /// Check if Ollama is running and accessible.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of available models from Ollama.
    /// </summary>
    Task<List<OllamaModel>> GetModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a streaming response using Ollama.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string model,
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a complete response using Ollama.
    /// </summary>
    Task<string> GenerateAsync(
        string model,
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Ollama model information.
/// </summary>
public class OllamaModel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("modified_at")]
    public DateTime ModifiedAt { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }

    [JsonPropertyName("details")]
    public OllamaModelDetails? Details { get; set; }

    /// <summary>
    /// Human-readable display name with parameter size.
    /// </summary>
    public string DisplayName => Details?.ParameterSize != null
        ? $"{Name} ({Details.ParameterSize})"
        : Name;
}

/// <summary>
/// Ollama model details.
/// </summary>
public class OllamaModelDetails
{
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("family")]
    public string? Family { get; set; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; set; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; set; }
}

/// <summary>
/// Ollama API response for /api/tags.
/// </summary>
public class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModel> Models { get; set; } = new();
}

/// <summary>
/// Ollama generation request.
/// </summary>
public class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; set; }
}

/// <summary>
/// Ollama generation options.
/// </summary>
public class OllamaOptions
{
    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; set; }
}

/// <summary>
/// Ollama streaming response chunk.
/// </summary>
public class OllamaGenerateResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; set; }

    [JsonPropertyName("load_duration")]
    public long? LoadDuration { get; set; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }

    [JsonPropertyName("eval_duration")]
    public long? EvalDuration { get; set; }
}

/// <summary>
/// Ollama service implementation.
/// </summary>
public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;
    private readonly string _baseUrl;
    private readonly string[] _defaultStopTokens = new[]
    {
        "<|im_end|>",
        "<|im_start|>",
        "<|endoftext|>",
        "</s>",
        "<|eot_id|>"
    };

    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger, string? baseUrl = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = baseUrl ?? Environment.GetEnvironmentVariable("OLLAMA_API_BASE_URL") ?? "http://localhost:11434";

        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama not available at {BaseUrl}", _baseUrl);
            return false;
        }
    }

    public async Task<List<OllamaModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();

            var tagsResponse = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken: cancellationToken);
            return tagsResponse?.Models ?? new List<OllamaModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Ollama models");
            return new List<OllamaModel>();
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string model,
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest
        {
            Model = model,
            Prompt = prompt,
            Stream = true,
            Options = new OllamaOptions
            {
                NumPredict = maxTokens ?? 2048,
                Temperature = temperature ?? 0.7f,
                Stop = _defaultStopTokens.ToList()
            }
        };

        var jsonContent = JsonSerializer.Serialize(request);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            OllamaGenerateResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line);
            }
            catch
            {
                continue;
            }

            if (chunk == null) continue;

            if (!string.IsNullOrEmpty(chunk.Response))
            {
                yield return chunk.Response;
            }

            if (chunk.Done)
            {
                _logger.LogDebug(
                    "Ollama generation complete: {EvalCount} tokens in {Duration}ms",
                    chunk.EvalCount,
                    chunk.EvalDuration / 1_000_000);
                break;
            }
        }
    }

    public async Task<string> GenerateAsync(
        string model,
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default)
    {
        var result = new StringBuilder();

        await foreach (var token in StreamAsync(model, prompt, maxTokens, temperature, cancellationToken))
        {
            result.Append(token);
        }

        return result.ToString().Trim();
    }
}
