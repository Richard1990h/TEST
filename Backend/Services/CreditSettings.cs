using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// Provides access to credit-related configuration settings from the database.
/// Uses caching for performance with periodic refresh.
/// </summary>
public sealed class CreditSettings
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CreditSettings> _logger;

    // Cache the settings with a refresh interval
    private CreditSystemSettings? _cachedSettings;
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(1);
    private readonly object _lock = new();

    public CreditSettings(IServiceScopeFactory scopeFactory, ILogger<CreditSettings> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get settings from cache or database
    /// </summary>
    private CreditSystemSettings GetSettings()
    {
        lock (_lock)
        {
            if (_cachedSettings != null && DateTime.UtcNow - _lastRefresh < _cacheExpiry)
                return _cachedSettings;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                _cachedSettings = db.CreditSystemSettings.FirstOrDefault(s => s.Id == 1);

                if (_cachedSettings == null)
                {
                    // Create default settings if not exists
                    _cachedSettings = new CreditSystemSettings { Id = 1 };
                    db.CreditSystemSettings.Add(_cachedSettings);
                    db.SaveChanges();
                    _logger.LogInformation("Created default credit system settings");
                }

                _lastRefresh = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load credit settings from database, using defaults");
                _cachedSettings ??= new CreditSystemSettings { Id = 1 };
            }

            return _cachedSettings;
        }
    }

    /// <summary>
    /// Force refresh settings from database
    /// </summary>
    public void RefreshCache()
    {
        lock (_lock)
        {
            _lastRefresh = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Free daily credits for non-subscribed users
    /// </summary>
    public double FreeDailyCredits => GetSettings().FreeDailyCredits;

    /// <summary>
    /// Hour (UTC) when daily credits reset
    /// </summary>
    public int DailyResetHourUtc => GetSettings().DailyResetHourUtc;

    /// <summary>
    /// Credits given to newly registered users
    /// </summary>
    public double NewUserCredits => GetSettings().NewUserCredits;

    /// <summary>
    /// Credits per chat message (flat cost)
    /// </summary>
    public double CostPerMessage => GetSettings().CostPerMessage;

    /// <summary>
    /// Credits per token used in LLM calls
    /// </summary>
    public double CostPerToken => GetSettings().CostPerToken;

    /// <summary>
    /// Base credits for project creation (before token costs)
    /// </summary>
    public double ProjectCreationBaseCost => GetSettings().ProjectCreationBaseCost;

    /// <summary>
    /// Credits per code analysis request
    /// </summary>
    public double CodeAnalysisCost => GetSettings().CodeAnalysisCost;

    /// <summary>
    /// Calculate credit cost based on token usage
    /// </summary>
    public double CalculateTokenCost(int totalTokens)
    {
        return totalTokens * CostPerToken;
    }

    /// <summary>
    /// Calculate total project creation cost (base + tokens)
    /// </summary>
    public double CalculateProjectCost(int totalTokens)
    {
        return ProjectCreationBaseCost + CalculateTokenCost(totalTokens);
    }
}
