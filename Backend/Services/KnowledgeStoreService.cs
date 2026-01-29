using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Services;

public sealed class KnowledgeStoreService
{
    private readonly ApplicationDbContext _db;

    public KnowledgeStoreService(ApplicationDbContext db)
    {
        _db = db;
    }

    public static string NormalizeKey(string input)
        => (input ?? "").Trim().ToLowerInvariant();

    public async Task<string?> TryLookupAsync(string userMessage)
    {
        var key = NormalizeKey(userMessage);

        // DB lookups must never throw and break the whole chat pipeline.
        // If schema is missing / migrations not applied, we safely return null
        // so deterministic solvers + web + LLM fallback can continue.
        try
        {
            // 1) Dictionary entries by key (case-normalized)
            var entry = await _db.KnowledgeEntries
                .AsNoTracking()
                .Where(k => k.Key.ToLower() == key)
                .OrderByDescending(k => k.Confidence)
                .FirstOrDefaultAsync();

            if (entry != null)
            {
                await TouchKnowledgeEntryAsync(entry.Id);
                return entry.Answer;
            }

            // 2) Aliases (EF-safe contains)
            entry = await _db.KnowledgeEntries
                .AsNoTracking()
                .Where(k => k.Aliases != null &&
                            EF.Functions.Like(k.Aliases, $"%{key}%"))
                .OrderByDescending(k => k.Confidence)
                .FirstOrDefaultAsync();

            if (entry != null)
            {
                await TouchKnowledgeEntryAsync(entry.Id);
                return entry.Answer;
            }

            // 3) Learned knowledge
            var learned = await _db.LearnedKnowledge
                .AsNoTracking()
                .Where(l => l.NormalizedKey == key)
                .OrderByDescending(l => l.Confidence)
                .FirstOrDefaultAsync();

            if (learned != null)
            {
                await TouchLearnedAsync(learned.Id);
                return learned.Answer;
            }

            return null;
        }
        catch
        {
            // Intentional silent fallback – never break chat pipeline
            return null;
        }
    }

    public async Task<FactEntry?> TryLookupFactAsync(string subject, string property)
    {
        subject = (subject ?? "").Trim();
        property = (property ?? "").Trim();

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(property))
            return null;

        var fact = await _db.FactEntries
            .AsNoTracking()
            .Where(f => f.Subject == subject && f.Property == property)
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefaultAsync();

        if (fact != null)
        {
            await TouchFactAsync(fact.Id);
        }

        return fact;
    }

    public async Task LearnAsync(string userMessage, string answer, string source = "llm", double confidence = 0.55)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(answer))
            return;

        var key = NormalizeKey(userMessage);

        // Don't learn extremely short or obvious chit-chat
        if (key.Length < 8)
            return;

        var existing = await _db.LearnedKnowledge
            .FirstOrDefaultAsync(l => l.NormalizedKey == key);

        if (existing == null)
        {
            _db.LearnedKnowledge.Add(new LearnedKnowledge
            {
                NormalizedKey = key,
                Question = userMessage.Trim(),
                Answer = answer.Trim(),
                Source = source,
                Confidence = confidence,
                TimesUsed = 0,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            // Update answer if we got a better one, keep the most recent
            existing.Answer = answer.Trim();
            existing.Source = source;
            existing.Confidence = Math.Max(existing.Confidence, confidence);
            existing.LastVerifiedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    private async Task TouchKnowledgeEntryAsync(int id)
    {
        var e = await _db.KnowledgeEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return;
        e.LastUsedAt = DateTime.UtcNow;
        e.TimesUsed += 1;
        await _db.SaveChangesAsync();
    }

    private async Task TouchLearnedAsync(int id)
    {
        var e = await _db.LearnedKnowledge.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return;
        e.LastUsedAt = DateTime.UtcNow;
        e.TimesUsed += 1;
        await _db.SaveChangesAsync();
    }

    private async Task TouchFactAsync(int id)
    {
        var e = await _db.FactEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return;
        e.LastUsedAt = DateTime.UtcNow;
        e.TimesUsed += 1;
        await _db.SaveChangesAsync();
    }
}
