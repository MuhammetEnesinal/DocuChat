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

    public async Task<QuestionCache?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        string? documentIds = null,
        string? documentContentHashes = null,
        CancellationToken ct = default)
    {
        var vector = new Vector(queryVector);

        var query = _db.QuestionCaches
            .Where(q => 1 - q.QuestionVector.CosineDistance(vector) >= threshold);

        if (!string.IsNullOrWhiteSpace(documentIds))
            query = query.Where(q => q.DocumentIds == documentIds);
        else
            query = query.Where(q => q.DocumentIds == null);

        // 1C: belge ContentHash'leri verilmişse → cache satırı aynı hash setine sahip olmalı.
        // Reprocess sonrası belgenin hash'i değişir → eski cache mismatch'le elenir.
        if (!string.IsNullOrWhiteSpace(documentContentHashes))
            query = query.Where(q => q.DocumentContentHashes == documentContentHashes);

        return await query
            .OrderByDescending(q => 1 - q.QuestionVector.CosineDistance(vector))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Aynı (normalize edilmiş QuestionText, DocumentIds) çifti varsa yeni satır eklemek yerine
    /// mevcut satırı tazeler (Answer/ImagesJson/Vector + HitCount++) — yarış koşulu veya
    /// embedding eşiği altı tekrarlarda çoğalmayı engeller.
    /// </summary>
    public async Task AddAsync(QuestionCache entry, CancellationToken ct = default)
    {
        var normalized = (entry.QuestionText ?? string.Empty).Trim().ToLower();
        var existing = await _db.QuestionCaches
            .Where(q => q.DocumentIds == entry.DocumentIds
                     && q.QuestionText.ToLower() == normalized)
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

    public async Task ClearByDocumentIdAsync(Guid docId, CancellationToken ct = default)
    {
        // DocumentIds formatı: ",<guid>,<guid>," (önce ve sonra virgül) — alt-string çakışması yok.
        var token = $",{docId},";
        await _db.QuestionCaches
            .Where(q => q.DocumentIds != null && q.DocumentIds.Contains(token))
            .ExecuteDeleteAsync(ct);
    }

    public async Task ClearByDocumentIdsAsync(IEnumerable<Guid> docIds, CancellationToken ct = default)
    {
        var tokens = docIds.Select(id => $",{id},").ToList();
        if (tokens.Count == 0) return;
        await _db.QuestionCaches
            .Where(q => q.DocumentIds != null && tokens.Any(t => q.DocumentIds.Contains(t)))
            .ExecuteDeleteAsync(ct);
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
