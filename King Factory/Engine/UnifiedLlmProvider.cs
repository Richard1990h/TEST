using LittleHelperAI.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LittleHelperAI.KingFactory.Engine;

/// <summary>
/// Unified interface for LLM inference that supports multiple providers.
/// </summary>
public interface IUnifiedLlmProvider
{
    /// <summary>
    /// Current provider being used.
    /// </summary>
    LlmProvider CurrentProvider { get; }

    /// <summary>
    /// Current model name.
    /// </summary>
    string? CurrentModel { get; }

    /// <summary>
    /// Whether the provider is ready for inference.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Whether the provider is currently generating.
    /// </summary>
    bool IsBusy { get; }

    /// <summary>
    /// Set the active provider and model.
    /// </summary>
    Task SetProviderAsync(LlmProvider provider, string? modelName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a streaming response.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a complete response (non-streaming).
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Count tokens in text (approximate for Ollama).
    /// </summary>
    int CountTokens(string text);
}

/// <summary>
/// Unified LLM provider that delegates to either local LLamaSharp or Ollama.
/// </summary>
public class UnifiedLlmProvider : IUnifiedLlmProvider
{
    private readonly ILlmEngine _localEngine;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<UnifiedLlmProvider> _logger;

    private LlmProvider _currentProvider = LlmProvider.Local;
    private string? _currentOllamaModel;
    private volatile bool _isBusy;

    public LlmProvider CurrentProvider => _currentProvider;
    public string? CurrentModel => _currentProvider == LlmProvider.Local
        ? _localEngine.CurrentModel
        : _currentOllamaModel;

    public bool IsReady => _currentProvider == LlmProvider.Local
        ? _localEngine.IsLoaded
        : !string.IsNullOrEmpty(_currentOllamaModel);

    public bool IsBusy => _isBusy || (_currentProvider == LlmProvider.Local && _localEngine.IsBusy);

    public UnifiedLlmProvider(
        ILlmEngine localEngine,
        IOllamaService ollamaService,
        ILogger<UnifiedLlmProvider> logger)
    {
        _localEngine = localEngine;
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task SetProviderAsync(LlmProvider provider, string? modelName = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Switching to provider {Provider}, model {Model}", provider, modelName);

        if (provider == LlmProvider.Local)
        {
            // Load local model
            if (!string.IsNullOrEmpty(modelName))
            {
                await _localEngine.LoadModelAsync(modelName, cancellationToken);
            }
            else if (!_localEngine.IsLoaded)
            {
                await _localEngine.LoadModelAsync(cancellationToken: cancellationToken);
            }

            _currentProvider = LlmProvider.Local;
            _currentOllamaModel = null;
        }
        else if (provider == LlmProvider.Ollama)
        {
            // Verify Ollama is available
            var available = await _ollamaService.IsAvailableAsync(cancellationToken);
            if (!available)
            {
                throw new InvalidOperationException("Ollama is not running");
            }

            // Verify model exists
            if (!string.IsNullOrEmpty(modelName))
            {
                var models = await _ollamaService.GetModelsAsync(cancellationToken);
                if (!models.Any(m => m.Name == modelName))
                {
                    throw new InvalidOperationException($"Model '{modelName}' not found in Ollama");
                }
            }

            // Unload local model to free memory
            if (_localEngine.IsLoaded)
            {
                await _localEngine.UnloadModelAsync();
            }

            _currentProvider = LlmProvider.Ollama;
            _currentOllamaModel = modelName;

            _logger.LogInformation("Switched to Ollama with model {Model}", modelName);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        int? maxTokens = null,
        float? temperature = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("No model loaded. Call SetProviderAsync first.");
        }

        _isBusy = true;
        var stopwatch = Stopwatch.StartNew();
        var ttftStopwatch = Stopwatch.StartNew();
        var tokenCount = 0;
        var firstTokenLogged = false;

        try
        {
            if (_currentProvider == LlmProvider.Local)
            {
                await foreach (var token in _localEngine.StreamAsync(prompt, maxTokens, temperature, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tokenCount++;
                    if (!firstTokenLogged)
                    {
                        firstTokenLogged = true;
                        _logger.LogInformation("TTFT ({Provider}): {TtftMs}ms", _currentProvider, ttftStopwatch.ElapsedMilliseconds);
                    }

                    if (tokenCount % 256 == 0)
                    {
                        var elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                        _logger.LogDebug("Tokens ({Provider}): {Tokens}, TPS: {TPS}", _currentProvider, tokenCount, tokenCount / elapsedSeconds);
                    }
                    yield return token;
                }
            }
            else if (_currentProvider == LlmProvider.Ollama)
            {
                if (string.IsNullOrEmpty(_currentOllamaModel))
                {
                    throw new InvalidOperationException("No Ollama model selected");
                }

                await foreach (var token in _ollamaService.StreamAsync(
                    _currentOllamaModel,
                    prompt,
                    maxTokens,
                    temperature,
                    cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tokenCount++;
                    if (!firstTokenLogged)
                    {
                        firstTokenLogged = true;
                        _logger.LogInformation("TTFT ({Provider}): {TtftMs}ms", _currentProvider, ttftStopwatch.ElapsedMilliseconds);
                    }

                    if (tokenCount % 256 == 0)
                    {
                        var elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                        _logger.LogDebug("Tokens ({Provider}): {Tokens}, TPS: {TPS}", _currentProvider, tokenCount, tokenCount / elapsedSeconds);
                    }
                    yield return token;
                }
            }
        }
        finally
        {
            var elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
            _logger.LogInformation("Request complete ({Provider}): {Tokens} tokens, {TPS} tokens/sec",
                _currentProvider, tokenCount, tokenCount / elapsedSeconds);
            _isBusy = false;
        }
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

    public int CountTokens(string text)
    {
        if (_currentProvider == LlmProvider.Local)
        {
            return _localEngine.CountTokens(text);
        }

        // Approximate token count for Ollama (roughly 4 chars per token)
        return string.IsNullOrEmpty(text) ? 0 : text.Length / 4;
    }
}
