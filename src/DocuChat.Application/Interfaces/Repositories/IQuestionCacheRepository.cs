using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public record CacheMatch(QuestionCache Cache, double Similarity);

public interface IQuestionCacheRepository
{
    // Similarity skorunu da döner — caller yüksek-sim hit'lerde ekstra validation atlayabilir.
    Task<CacheMatch?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        CancellationToken ct = default);

    Task AddAsync(QuestionCache entry, CancellationToken ct = default);
    Task IncrementHitAsync(Guid id, CancellationToken ct = default);

    /// Tüm cache'i temizler. Belge içeriği değişiminde (upload/reprocess/delete) çağrılır —
    Task ClearAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default);

    /// maxAge süresi geçmiş, hiç kullanılmayan cache kayıtlarını siler.
    Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default);
}
