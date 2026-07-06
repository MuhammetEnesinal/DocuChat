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
    // Cosine similarity ile en yakın cache eşleşmesini döner (threshold altıysa null).
    // Similarity skoru da döner — caller yüksek-sim hit'lerde ekstra validation atlayabilir.
    Task<CacheMatch?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        CancellationToken ct = default);

    // Upsert: aynı normalize edilmiş QuestionText varsa cevap/vector güncellenir ve HitCount++,
    // yoksa yeni kayıt eklenir. SaveChanges UoW'da çağrılmalı.
    // (IRepository.AddAsync vanilla INSERT yapar — bu metot upsert semantic için ayrı.)
    Task UpsertAsync(QuestionCache entry, CancellationToken ct = default);

    Task IncrementHitAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default);

    // maxAge süresi geçmiş, hiç kullanılmayan cache kayıtlarını siler.
    Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default);

    // Per-document invalidation: SourceDocumentIds CSV'sinde verilen ID'yi içeren cache entry'leri siler.
    // includeUntracked=true ise SourceDocumentIds=NULL (kaynağı bilinmeyen) kayıtlar da silinir.
    Task<int> DeleteByDocumentIdAsync(Guid documentId, bool includeUntracked, CancellationToken ct = default);
}
