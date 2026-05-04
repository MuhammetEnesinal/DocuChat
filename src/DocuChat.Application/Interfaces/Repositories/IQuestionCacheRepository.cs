using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IQuestionCacheRepository
{
    /// Verilen vektöre semantik olarak benzer ve aynı belge kombinasyonuna ait cache döner.
    /// documentIds null ise belge filtresi uygulanmaz.

    Task<QuestionCache?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        string? documentIds = null,
        CancellationToken ct = default);

    Task AddAsync(QuestionCache entry, CancellationToken ct = default);
    Task IncrementHitAsync(Guid id, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
}