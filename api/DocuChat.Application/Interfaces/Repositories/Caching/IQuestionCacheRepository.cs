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

namespace DocuChat.Application.Interfaces.Repositories.Caching;

public class CacheMatch
{
    public QuestionCache Cache { get; set; }
    public double Similarity { get; set; }

    public CacheMatch(QuestionCache Cache, double Similarity)
    {
        this.Cache = Cache;
        this.Similarity = Similarity;
    }
}

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

    Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default);

    /// <summary>maxAge süresi geçmiş, hiç kullanılmayan cache kayıtlarını siler.</summary>
    Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default);

    /// <summary>
    /// Per-document invalidation: SourceDocumentIds CSV'sinde verilen ID'yi içeren cache entry'leri siler.
    /// includeUntracked=true ise SourceDocumentIds=NULL olan eski entries de silinir (geriye uyumluluk).
    /// </summary>
    Task<int> DeleteByDocumentIdAsync(Guid documentId, bool includeUntracked, CancellationToken ct = default);
}
