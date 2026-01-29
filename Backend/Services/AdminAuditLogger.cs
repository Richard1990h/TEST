using System.Text.Json;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// ADD-ONLY audit logger that stores admin actions in the existing KnowledgeEntries table
/// (category = "audit") so we do NOT need new DB tables.
/// </summary>
public sealed class AdminAuditLogger
{
    private readonly ApplicationDbContext _db;

    public AdminAuditLogger(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        int actorUserId,
        string action,
        object? details = null,
        CancellationToken ct = default)
    {
        var utc = DateTime.UtcNow;
        var key = $"audit:{utc:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";

        var payload = new
        {
            actorUserId,
            action,
            utc,
            details
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        _db.KnowledgeEntries.Add(new KnowledgeEntry
        {
            Key = key,
            Category = "audit",
            Answer = json,
            Aliases = "",
            Confidence = 1.0,
            Source = "admin",
            CreatedAt = utc,
            UpdatedAt = utc,
            LastUsedAt = utc,
            TimesUsed = 1
        });

        await _db.SaveChangesAsync(ct);
    }
}
