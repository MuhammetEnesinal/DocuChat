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
    // departmentIds: departman izolasyonu. null = filtre yok (admin, tüm kayıtlar); doluysa
    //   yalnız DepartmentId'si bu kümede olan kayıtlar (global/null kapsamlılar hariç).
    Task<CacheMatch?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        IReadOnlyList<Guid>? departmentIds = null,
        CancellationToken ct = default);

    // Upsert: aynı departman kapsamında aynı normalize edilmiş QuestionText varsa cevap, vektör
    // ve kaynak belge listesi güncellenir; yoksa yeni kayıt eklenir. HitCount ve LastHitAt'e
    // dokunulmaz — kullanım sayacı yalnız cache'ten cevap servis edildiğinde artar
    // (IncrementHitAsync). SaveChanges UoW'da çağrılmalı.
    // (IRepository.AddAsync vanilla INSERT yapar — bu metot upsert semantic için ayrı.)
    Task UpsertAsync(QuestionCache entry, CancellationToken ct = default);

    Task IncrementHitAsync(Guid id, CancellationToken ct = default);

    // Popüler sorular. departmentIds: departman izolasyonu — null = filtre yok (admin); doluysa
    // yalnız o departmanlara etiketli kayıtlar. Soru METİNLERİ de bilgi sızdırır, filtre şart.
    Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default);

    // maxAge süresi geçmiş, hiç kullanılmayan cache kayıtlarını siler.
    Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default);

    // Per-document invalidation: SourceDocumentIds CSV'sinde verilen ID'yi içeren cache entry'leri siler.
    // includeUntracked=true ise SourceDocumentIds=NULL (kaynağı bilinmeyen) kayıtlar da silinir.
    Task<int> DeleteByDocumentIdAsync(Guid documentId, bool includeUntracked, CancellationToken ct = default);
}
