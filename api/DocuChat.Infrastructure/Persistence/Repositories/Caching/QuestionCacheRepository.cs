using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Repositories;
using DocuChat.Infrastructure.Persistence.Repositories.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Interfaces.Repositories.Common;
using DocuChat.Application.Interfaces.Repositories.Chat;
using DocuChat.Application.Interfaces.Repositories.Documents;
using DocuChat.Application.Interfaces.Repositories.Caching;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;

namespace DocuChat.Infrastructure.Persistence.Repositories.Caching;

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
        IReadOnlyList<Guid>? departmentIds = null,
        CancellationToken ct = default)
    {
        var vector = new Vector(queryVector);

        var query = _set
            .Where(q => 1 - q.QuestionVector.CosineDistance(vector) >= threshold);

        // Departman izolasyonu: null = admin (filtre yok, global/null kapsamlılar dahil her şey).
        // Doluysa yalnız kullanıcının departmanlarına etiketli kayıtlar; global (null) kayıtlar
        // normal kullanıcıya servis edilmez → A departmanının cevabı B'ye sızmaz.
        if (departmentIds is not null)
            query = query.Where(q => q.DepartmentId != null && departmentIds.Contains(q.DepartmentId.Value));

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

    // Upsert: aynı normalize edilmiş QuestionText varsa cevap/vector güncellenir,
    // yoksa yeni entry eklenir. Vanilla INSERT için IRepository.AddAsync kullanılır.
    // HitCount/LastHitAt'e DOKUNULMAZ — hit yalnız cache'ten servis edildiğinde sayılır
    // (IncrementHitAsync); cevap yenilemek kullanım değildir, popüler-soru sıralamasını şişirir.
    public async Task UpsertAsync(QuestionCache entry, CancellationToken ct = default)
    {
        var normalized = (entry.QuestionText ?? string.Empty).Trim().ToLower();
        // Upsert anahtarı departman bazlı: aynı soru farklı departmanlarda farklı cevaplar
        // tutar → biri diğerini ezmez (izolasyon). DepartmentId eşitliği null-güvenli kıyaslanır.
        var existing = await _set
            .Where(q => q.QuestionText.ToLower() == normalized && q.DepartmentId == entry.DepartmentId)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.Answer = entry.Answer;
            existing.ImagesJson = entry.ImagesJson;
            existing.QuestionVector = entry.QuestionVector;
            // Yeni cevap farklı chunks'tan üretilmiş olabilir → source list de güncellenir.
            // (Per-document invalidation doğru çalışsın diye.)
            existing.SourceDocumentIds = entry.SourceDocumentIds;
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

    public async Task<IReadOnlyList<string>> GetTopByHitCountAsync(
        int limit, IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default)
    {
        var query = _set.AsQueryable();

        // Departman izolasyonu: null = admin (hepsi). Doluysa yalnız kullanıcının departmanlarına
        // etiketli kayıtlar; global (null) kayıtlar normal kullanıcıya gösterilmez.
        if (departmentIds is not null)
            query = query.Where(q => q.DepartmentId != null && departmentIds.Contains(q.DepartmentId.Value));

        return await query
            .OrderByDescending(q => q.HitCount)
            .Take(limit)
            .Select(q => q.QuestionText)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        // Hit almamış cache de eskidiğinde silinmeli — bu yüzden CreatedAt'i de kontrol et.
        var cutoff = DateTime.UtcNow - maxAge;
        return await _set
            .Where(q => q.CreatedAt < cutoff
                     && (q.LastHitAt == null || q.LastHitAt < cutoff))
            .ExecuteDeleteAsync(ct);
    }

    // Per-document cache invalidation:
    // - SourceDocumentIds CSV'sinde documentId geçen entries silinir (PostgreSQL ILIKE)
    // - includeUntracked=true: SourceDocumentIds=NULL (kaynağı bilinmeyen) kayıtlar da silinir
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
