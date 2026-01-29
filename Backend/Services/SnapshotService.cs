using System.Text.Json;
using LittleHelperAI.Data;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// ADD-ONLY: creates "hard copy" JSON snapshots of operational tables (plans/policies/subscriptions/daily state).
/// Stored on disk so admins can export/retain evidence without DB schema changes.
/// </summary>
public sealed class SnapshotService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SnapshotService> _logger;

    public SnapshotService(ApplicationDbContext db, IWebHostEnvironment env, ILogger<SnapshotService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public string SnapshotDirectory
    {
        get
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "snapshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public async Task<(string fileName, string fullPath)> CreateSnapshotAsync(CancellationToken ct = default)
    {
        var utc = DateTime.UtcNow;
        var fileName = $"snapshot_{utc:yyyyMMdd_HHmmss}_utc.json";
        var fullPath = Path.Combine(SnapshotDirectory, fileName);

        var payload = new
        {
            utc,
            stripePlans = await _db.StripePlans.AsNoTracking().ToListAsync(ct),
            stripePlanPolicies = await _db.StripePlanPolicies.AsNoTracking().ToListAsync(ct),
            userStripeSubscriptions = await _db.UserStripeSubscriptions.AsNoTracking().ToListAsync(ct),
            userDailyCreditState = await _db.UserDailyCreditStates.AsNoTracking().ToListAsync(ct)
        };

        await using var fs = File.Create(fullPath);
        await JsonSerializer.SerializeAsync(fs, payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }, ct);

        _logger.LogInformation("[SNAPSHOT] Created {File}", fullPath);
        return (fileName, fullPath);
    }

    public IEnumerable<(string fileName, DateTime createdUtc, long bytes)> ListSnapshots()
    {
        var dir = SnapshotDirectory;
        return Directory.EnumerateFiles(dir, "snapshot_*.json")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => (f.Name, f.CreationTimeUtc, f.Length));
    }

    public string? GetSnapshotPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        fileName = Path.GetFileName(fileName); // prevent traversal
        var full = Path.Combine(SnapshotDirectory, fileName);
        return File.Exists(full) ? full : null;
    }

    public void PruneOlderThan(TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var (fileName, createdUtc, _) in ListSnapshots())
            {
                if (createdUtc < cutoff)
                {
                    var path = GetSnapshotPath(fileName);
                    if (path != null)
                        File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SNAPSHOT] Prune failed");
        }
    }
}
