using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IQuestionCacheRepository
{
    Task<QuestionCache?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        string? documentIds = null,
        CancellationToken ct = default);

    Task AddAsync(QuestionCache entry, CancellationToken ct = default);
    Task IncrementHitAsync(Guid id, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);

    /// Belirtilen belge ID'sini içeren cache kayıtlarını siler.
    Task ClearByDocumentIdAsync(Guid docId, CancellationToken ct = default);

    /// Belirtilen belge ID'lerinden herhangi birini içeren cache kayıtlarını siler.
    Task ClearByDocumentIdsAsync(IEnumerable<Guid> docIds, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default);

    /// maxAge süresi geçmiş, hiç kullanılmayan cache kayıtlarını siler.
    Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default);
}
