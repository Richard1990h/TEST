using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Backend.Services;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/knowledge")]
public sealed class AdminKnowledgeController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AdminAuditLogger _audit;

    public AdminKnowledgeController(
        ApplicationDbContext db,
        AdminAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    // =========================
    // KNOWLEDGE ENTRIES
    // =========================
    [HttpGet("entries")]
    public async Task<IActionResult> ListEntries(
        [FromQuery] string? q = null,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var query = _db.KnowledgeEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();

            query = query.Where(x =>
                x.Key.Contains(s) ||
                x.Category.Contains(s) ||
                x.Answer.Contains(s) ||
                (x.Aliases != null && x.Aliases.Contains(s)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return Ok(new { total, items });
    }

    [HttpPost("entries")]
    public async Task<IActionResult> UpsertEntry(
        [FromBody] KnowledgeEntry entry,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Key) ||
            string.IsNullOrWhiteSpace(entry.Answer))
            return BadRequest("Key and Answer are required.");

        entry.Key = entry.Key.Trim().ToLowerInvariant();
        entry.Category = string.IsNullOrWhiteSpace(entry.Category)
            ? "general"
            : entry.Category.Trim().ToLowerInvariant();

        entry.Source = string.IsNullOrWhiteSpace(entry.Source)
            ? "manual"
            : entry.Source;

        entry.UpdatedAt = DateTime.UtcNow;

        if (entry.Id == 0)
        {
            entry.CreatedAt = DateTime.UtcNow;
            _db.KnowledgeEntries.Add(entry);

            await _audit.LogAsync(
                GetUserId(),
                "knowledge.create",
                new { entry.Key, entry.Category },
                ct);
        }
        else
        {
            var existing = await _db.KnowledgeEntries
                .FirstOrDefaultAsync(x => x.Id == entry.Id, ct);

            if (existing == null)
                return NotFound();

            existing.Key = entry.Key;
            existing.Category = entry.Category;
            existing.Answer = entry.Answer;
            existing.Aliases = entry.Aliases ?? "";
            existing.Confidence = entry.Confidence;
            existing.Source = entry.Source;
            existing.UpdatedAt = DateTime.UtcNow;

            await _audit.LogAsync(
                GetUserId(),
                "knowledge.update",
                new { existing.Id, existing.Key },
                ct);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(entry);
    }

    [HttpDelete("entries/{id:int}")]
    public async Task<IActionResult> DeleteEntry(
        int id,
        CancellationToken ct = default)
    {
        var existing = await _db.KnowledgeEntries
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (existing == null)
            return NotFound();

        _db.KnowledgeEntries.Remove(existing);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            GetUserId(),
            "knowledge.delete",
            new { id, existing.Key },
            ct);

        return Ok(new { deleted = true });
    }

    // =========================
    // LEARNED KNOWLEDGE
    // =========================
    [HttpGet("learned")]
    public async Task<IActionResult> ListLearned(
        [FromQuery] string? q = null,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var query = _db.LearnedKnowledge.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();

            query = query.Where(x =>
                x.NormalizedKey.Contains(s) ||
                x.Question.Contains(s) ||
                x.Answer.Contains(s));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return Ok(new { total, items });
    }

    [HttpDelete("learned/{id:int}")]
    public async Task<IActionResult> DeleteLearned(
        int id,
        CancellationToken ct = default)
    {
        var existing = await _db.LearnedKnowledge
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (existing == null)
            return NotFound();

        _db.LearnedKnowledge.Remove(existing);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            GetUserId(),
            "learned_knowledge.delete",
            new { id },
            ct);

        return Ok(new { deleted = true });
    }

    // =========================
    // HELPERS
    // =========================
    private int GetUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
