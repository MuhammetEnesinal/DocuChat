// DocuChat.Infrastructure/Persistence/Repositories/QuestionCacheRepository.cs
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;
using DocuChat.Infrastructure.Persistence;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class QuestionCacheRepository : IQuestionCacheRepository
{
    private readonly AppDbContext _db;

    public QuestionCacheRepository(AppDbContext db) => _db = db;

    public async Task<CacheMatch?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        CancellationToken ct = default)
    {
        var vector = new Vector(queryVector);

        // Arama global → cache global. Belge filtresi yok; içerik değişiminde tüm cache temizlenir.
        var query = _db.QuestionCaches
            .Where(q => 1 - q.QuestionVector.CosineDistance(vector) >= threshold);

        // En yakın 3 adayı çek (debug için), en yüksek skoru döndür.
        var candidates = await query
            .Select(q => new
            {
                Cache = q,
                Sim = 1 - q.QuestionVector.CosineDistance(vector)
            })
            .OrderByDescending(x => x.Sim)
            .Take(3)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            // Eşik altı veya hiç entry yok — debug için en yakını da göster
            var nearest = await _db.QuestionCaches
                .Select(q => new
                {
                    Q = q.QuestionText,
                    Sim = 1 - q.QuestionVector.CosineDistance(vector)
                })
                .OrderByDescending(x => x.Sim)
                .Take(1)
                .FirstOrDefaultAsync(ct);

            if (nearest != null)
                Console.WriteLine($"[CacheDebug] MISS — threshold={threshold:F3}, en yakın aday sim={nearest.Sim:F3} q='{nearest.Q}'");
            return null;
        }

        var best = candidates[0];
        Console.WriteLine($"[CacheDebug] HIT — threshold={threshold:F3}, sim={best.Sim:F3} q='{best.Cache.QuestionText}'");
        return new CacheMatch(best.Cache, best.Sim);
    }

    public async Task AddAsync(QuestionCache entry, CancellationToken ct = default)
    {
        var normalized = (entry.QuestionText ?? string.Empty).Trim().ToLower();
        var existing = await _db.QuestionCaches
            .Where(q => q.QuestionText.ToLower() == normalized)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.Answer = entry.Answer;
            existing.ImagesJson = entry.ImagesJson;
            existing.QuestionVector = entry.QuestionVector;
            existing.HitCount += 1;
            existing.LastHitAt = DateTime.UtcNow;
            return;
        }

        _db.QuestionCaches.Add(entry);
    }

    public async Task IncrementHitAsync(Guid id, CancellationToken ct = default)
    {
        await _db.QuestionCaches
            .Where(q => q.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.HitCount, q => q.HitCount + 1)
                .SetProperty(q => q.LastHitAt, _ => DateTime.UtcNow),
                ct);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _db.QuestionCaches.ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default)
        => await _db.QuestionCaches
            .OrderByDescending(q => q.HitCount)
            .Take(limit)
            .Select(q => q.QuestionText)
            .ToListAsync(ct);

    public async Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        // Hit almamış cache de eskidiğinde silinmeli — bu yüzden CreatedAt'i de kontrol et.
        var cutoff = DateTime.UtcNow - maxAge;
        return await _db.QuestionCaches
            .Where(q => q.CreatedAt < cutoff
                     && (q.LastHitAt == null || q.LastHitAt < cutoff))
            .ExecuteDeleteAsync(ct);
    }
}
