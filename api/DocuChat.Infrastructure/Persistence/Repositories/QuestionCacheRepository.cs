using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class QuestionCacheRepository : GenericRepository<QuestionCache>, IQuestionCacheRepository
{
    private readonly ILogger<QuestionCacheRepository> _logger;

    public QuestionCacheRepository(AppDbContext db, ILogger<QuestionCacheRepository> logger)
        : base(db)
    {
        _logger = logger;
    }

    public async Task<CacheMatch?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        CancellationToken ct = default)
    {
        var vector = new Vector(queryVector);

        // Arama global → cache global. Belge filtresi yok; içerik değişiminde tüm cache temizlenir.
        var query = _set
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
            var nearest = await _set
                .Select(q => new
                {
                    Q = q.QuestionText,
                    Sim = 1 - q.QuestionVector.CosineDistance(vector)
                })
                .OrderByDescending(x => x.Sim)
                .Take(1)
                .FirstOrDefaultAsync(ct);

            if (nearest != null)
                _logger.LogInformation(
                    "[Cache] MISS — threshold={Threshold:F3}, en yakın aday sim={Similarity:F3} q='{Question}'",
                    threshold, nearest.Sim, nearest.Q);
            return null;
        }

        var best = candidates[0];
        _logger.LogInformation(
            "[Cache] HIT — threshold={Threshold:F3}, sim={Similarity:F3} q='{Question}'",
            threshold, best.Sim, best.Cache.QuestionText);
        return new CacheMatch(best.Cache, best.Sim);
    }

    /// <summary>
    /// Upsert: aynı normalize edilmiş QuestionText varsa cevap/vector güncellenir + HitCount++,
    /// yoksa yeni entry eklenir. Vanilla INSERT için IRepository.AddAsync kullanılır.
    /// </summary>
    public async Task UpsertAsync(QuestionCache entry, CancellationToken ct = default)
    {
        var normalized = (entry.QuestionText ?? string.Empty).Trim().ToLower();
        var existing = await _set
            .Where(q => q.QuestionText.ToLower() == normalized)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.Answer = entry.Answer;
            existing.ImagesJson = entry.ImagesJson;
            existing.QuestionVector = entry.QuestionVector;
            // Yeni cevap farklı chunks'tan üretilmiş olabilir → source list de güncellenir.
            // (Per-document invalidation doğru çalışsın diye.)
            existing.SourceDocumentIds = entry.SourceDocumentIds;
            existing.HitCount += 1;
            existing.LastHitAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            return;
        }

        _set.Add(entry);
    }

    public async Task IncrementHitAsync(Guid id, CancellationToken ct = default)
    {
        await _set
            .Where(q => q.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.HitCount, q => q.HitCount + 1)
                .SetProperty(q => q.LastHitAt, _ => DateTime.UtcNow),
                ct);
    }

    public async Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default)
        => await _set
            .OrderByDescending(q => q.HitCount)
            .Take(limit)
            .Select(q => q.QuestionText)
            .ToListAsync(ct);

    public async Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        // Hit almamış cache de eskidiğinde silinmeli — bu yüzden CreatedAt'i de kontrol et.
        var cutoff = DateTime.UtcNow - maxAge;
        return await _set
            .Where(q => q.CreatedAt < cutoff
                     && (q.LastHitAt == null || q.LastHitAt < cutoff))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Per-document cache invalidation:
    ///   - SourceDocumentIds CSV'sinde documentId geçen entries silinir (PostgreSQL ILIKE)
    ///   - includeUntracked=true: SourceDocumentIds=NULL olan (eski) entries de silinir
    ///     (geriye uyumluluk — eski cache'de source tracking yoktu)
    /// </summary>
    public async Task<int> DeleteByDocumentIdAsync(Guid documentId, bool includeUntracked, CancellationToken ct = default)
    {
        var idStr = documentId.ToString();
        var query = _set.AsQueryable();

        if (includeUntracked)
        {
            // CSV içinde GUID match veya untracked (NULL)
            query = query.Where(q =>
                q.SourceDocumentIds == null ||
                EF.Functions.ILike(q.SourceDocumentIds, "%" + idStr + "%"));
        }
        else
        {
            // Sadece tracked entries içinde match
            query = query.Where(q =>
                q.SourceDocumentIds != null &&
                EF.Functions.ILike(q.SourceDocumentIds, "%" + idStr + "%"));
        }

        return await query.ExecuteDeleteAsync(ct);
    }
}
