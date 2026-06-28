using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public record CacheMatch(QuestionCache Cache, double Similarity);

public interface IQuestionCacheRepository : IRepository<QuestionCache>
{
    /// <summary>
    /// Cosine similarity ile en yakın cache eşleşmesini döner (threshold altıysa null).
    /// Similarity skoru da döner — caller yüksek-sim hit'lerde ekstra validation atlayabilir.
    /// </summary>
    Task<CacheMatch?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        CancellationToken ct = default);

    /// <summary>
    /// Upsert: aynı normalize edilmiş QuestionText varsa cevap/vector güncellenir ve HitCount++,
    /// yoksa yeni kayıt eklenir. SaveChanges UoW'da çağrılmalı.
    /// (IRepository.AddAsync vanilla INSERT yapar — bu metot upsert semantic için ayrı.)
    /// </summary>
    Task UpsertAsync(QuestionCache entry, CancellationToken ct = default);

    Task IncrementHitAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tüm cache'i temizler. Toplu reset için (örn. global config değişikliği).</summary>
    Task ClearAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default);

    /// <summary>maxAge süresi geçmiş, hiç kullanılmayan cache kayıtlarını siler.</summary>
    Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default);

    /// <summary>
    /// Per-document invalidation: SourceDocumentIds CSV'sinde verilen ID'yi içeren cache entry'leri siler.
    /// includeUntracked=true ise SourceDocumentIds=NULL olan eski entries de silinir (geriye uyumluluk).
    /// </summary>
    Task<int> DeleteByDocumentIdAsync(Guid documentId, bool includeUntracked, CancellationToken ct = default);
}
