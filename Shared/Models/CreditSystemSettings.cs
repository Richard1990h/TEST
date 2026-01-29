namespace LittleHelperAI.Shared.Models;

/// <summary>
/// Stores the credit system settings configurable by admin.
/// Singleton table (id=1) for system-wide credit configuration.
/// </summary>
public class CreditSystemSettings
{
    public int Id { get; set; } = 1; // Singleton row

    /// <summary>
    /// Free daily credits for non-subscribed users
    /// </summary>
    public double FreeDailyCredits { get; set; } = 50.0;

    /// <summary>
    /// Hour (UTC) when daily credits reset (0-23)
    /// </summary>
    public int DailyResetHourUtc { get; set; } = 0;

    /// <summary>
    /// Credits given to newly registered users
    /// </summary>
    public double NewUserCredits { get; set; } = 50.0;

    /// <summary>
    /// Credits per chat message (flat cost)
    /// </summary>
    public double CostPerMessage { get; set; } = 0.01;

    /// <summary>
    /// Credits per token used in LLM calls
    /// </summary>
    public double CostPerToken { get; set; } = 0.001;

    /// <summary>
    /// Base credits for project creation (before token costs)
    /// </summary>
    public double ProjectCreationBaseCost { get; set; } = 1.0;

    /// <summary>
    /// Credits per code analysis request
    /// </summary>
    public double CodeAnalysisCost { get; set; } = 0.5;

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// DTO for updating credit system settings
/// </summary>
public class UpdateCreditSystemSettingsRequest
{
    public double FreeDailyCredits { get; set; }
    public int DailyResetHourUtc { get; set; }
    public double NewUserCredits { get; set; }
    public double CostPerMessage { get; set; }
    public double CostPerToken { get; set; }
    public double ProjectCreationBaseCost { get; set; }
    public double CodeAnalysisCost { get; set; }
}
