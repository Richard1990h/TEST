using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/settings")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AdminAuditLogger _audit;
    private readonly CreditSettings _creditSettings;

    public AdminSettingsController(
        ApplicationDbContext db,
        AdminAuditLogger audit,
        CreditSettings creditSettings)
    {
        _db = db;
        _audit = audit;
        _creditSettings = creditSettings;
    }

    // =========================
    // GET SETTINGS
    // =========================
    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var settings = await _db.CreditSystemSettings
            .FirstOrDefaultAsync(s => s.Id == 1, ct);

        if (settings == null)
        {
            // Create default settings if not exists
            settings = new CreditSystemSettings { Id = 1 };
            _db.CreditSystemSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            settings.FreeDailyCredits,
            settings.DailyResetHourUtc,
            settings.NewUserCredits,
            settings.CostPerMessage,
            settings.CostPerToken,
            settings.ProjectCreationBaseCost,
            settings.CodeAnalysisCost
        });
    }

    // =========================
    // SAVE SETTINGS
    // =========================
    [HttpPost]
    public async Task<IActionResult> SaveSettings(
        [FromBody] SettingsRequest request,
        CancellationToken ct)
    {
        try
        {
            var settings = await _db.CreditSystemSettings
                .FirstOrDefaultAsync(s => s.Id == 1, ct);

            if (settings == null)
            {
                // Create if not exists
                settings = new CreditSystemSettings { Id = 1 };
                _db.CreditSystemSettings.Add(settings);
            }

            // Update all fields
            settings.FreeDailyCredits = request.FreeDailyCredits;
            settings.DailyResetHourUtc = request.DailyResetHourUtc;
            settings.NewUserCredits = request.NewUserCredits;
            settings.CostPerMessage = request.CostPerMessage;
            settings.CostPerToken = request.CostPerToken;
            settings.ProjectCreationBaseCost = request.ProjectCreationBaseCost;
            settings.CodeAnalysisCost = request.CodeAnalysisCost;
            settings.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            // Refresh the cached settings
            _creditSettings.RefreshCache();

            // 🔍 Audit admin config change
            await _audit.LogAsync(
                GetActorUserId(),
                "settings.update",
                new
                {
                    request.FreeDailyCredits,
                    request.DailyResetHourUtc,
                    request.NewUserCredits,
                    request.CostPerMessage,
                    request.CostPerToken,
                    request.ProjectCreationBaseCost,
                    request.CodeAnalysisCost
                },
                ct
            );

            return Ok(new
            {
                message = "Settings saved successfully. Changes take effect immediately."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error saving settings: {ex.Message}");
        }
    }

    // =========================
    // DTO
    // =========================
    public sealed class SettingsRequest
    {
        public double FreeDailyCredits { get; set; }
        public int DailyResetHourUtc { get; set; }
        public double NewUserCredits { get; set; }
        public double CostPerMessage { get; set; }
        public double CostPerToken { get; set; }
        public double ProjectCreationBaseCost { get; set; }
        public double CodeAnalysisCost { get; set; }
    }

    // =========================
    // HELPERS
    // =========================
    private int GetActorUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
