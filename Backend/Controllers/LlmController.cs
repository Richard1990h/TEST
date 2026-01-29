using Microsoft.AspNetCore.Mvc;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LlmController : ControllerBase
{
    private readonly ILlmEngine _llmEngine;
    private readonly IOllamaService _ollamaService;
    private readonly IUnifiedLlmProvider _unifiedProvider;
    private readonly ILogger<LlmController> _logger;

    public LlmController(
        ILlmEngine llmEngine,
        IOllamaService ollamaService,
        IUnifiedLlmProvider unifiedProvider,
        ILogger<LlmController> logger)
    {
        _llmEngine = llmEngine;
        _ollamaService = ollamaService;
        _unifiedProvider = unifiedProvider;
        _logger = logger;
    }

    /// <summary>
    /// Get current model info and available models.
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        try
        {
            var availableModels = _llmEngine.GetAvailableModels();

            return Ok(new ModelInfoResponse
            {
                CurrentModel = _unifiedProvider.CurrentModel ?? string.Empty,
                CurrentProvider = _unifiedProvider.CurrentProvider,
                AvailableModels = availableModels.ToList(),
                Metadata = $"Provider: {_unifiedProvider.CurrentProvider}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get LLM info");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get all available providers and their models.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders()
    {
        try
        {
            var providers = new List<ProviderInfo>();

            // Local provider (LLamaSharp/GGUF)
            var localModels = _llmEngine.GetAvailableModels();
            providers.Add(new ProviderInfo
            {
                Provider = LlmProvider.Local,
                Name = "Local (GGUF)",
                IsAvailable = true,
                Models = localModels.Select(m => new Shared.Models.ModelInfo
                {
                    Name = m,
                    DisplayName = m,
                    Provider = LlmProvider.Local
                }).ToList()
            });

            // Ollama provider
            var ollamaAvailable = await _ollamaService.IsAvailableAsync();
            var ollamaProvider = new ProviderInfo
            {
                Provider = LlmProvider.Ollama,
                Name = "Ollama",
                IsAvailable = ollamaAvailable,
                Models = new List<Shared.Models.ModelInfo>()
            };

            if (ollamaAvailable)
            {
                var ollamaModels = await _ollamaService.GetModelsAsync();
                ollamaProvider.Models = ollamaModels.Select(m => new Shared.Models.ModelInfo
                {
                    Name = m.Name,
                    DisplayName = m.DisplayName,
                    Provider = LlmProvider.Ollama,
                    SizeBytes = m.Size,
                    ParameterSize = m.Details?.ParameterSize
                }).ToList();
            }

            providers.Add(ollamaProvider);

            return Ok(new ProvidersResponse
            {
                CurrentProvider = _unifiedProvider.CurrentProvider,
                CurrentModel = _unifiedProvider.CurrentModel,
                Providers = providers
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get providers");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Load a specific model.
    /// </summary>
    [HttpPost("load")]
    public async Task<IActionResult> LoadModel([FromBody] ModelSelectionRequest request)
    {
        try
        {
            _logger.LogInformation("Loading model: {ModelName} from provider: {Provider}",
                request.ModelName, request.Provider);

            // Use the unified provider to switch providers/models
            await _unifiedProvider.SetProviderAsync(request.Provider, request.ModelName);

            return Ok(new
            {
                Success = true,
                Provider = _unifiedProvider.CurrentProvider,
                CurrentModel = _unifiedProvider.CurrentModel,
                Message = $"Model '{_unifiedProvider.CurrentModel}' loaded successfully via {_unifiedProvider.CurrentProvider}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model: {ModelName}", request.ModelName);
            return StatusCode(500, new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Reload the current model.
    /// </summary>
    [HttpPost("reload")]
    public async Task<IActionResult> ReloadModel()
    {
        try
        {
            await _llmEngine.LoadModelAsync();
            return Ok(new { Success = true, CurrentModel = _llmEngine.CurrentModel });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload model");
            return StatusCode(500, new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Reinitialize the LLM engine.
    /// </summary>
    [HttpPost("reinit")]
    public async Task<IActionResult> Reinitialize()
    {
        try
        {
            await _llmEngine.UnloadModelAsync();
            await _llmEngine.LoadModelAsync();
            return Ok(new { Success = true, CurrentModel = _llmEngine.CurrentModel });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reinitialize LLM");
            return StatusCode(500, new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Get engine statistics.
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        try
        {
            var stats = _llmEngine.GetStats();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stats");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

}
