using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IChatMessageFeedbackRepository : IRepository<ChatMessageFeedback>
{
    /// <summary>Belirli user-message kombinasyonu için feedback var mı? (UNIQUE check)</summary>
    Task<bool> ExistsByUserAndMessageAsync(string userId, Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının soru benzerliği eşleşen TÜM feedback'lerini (like + dislike) getirir.
    /// Clustering ve net dislike count C# katmanında yapılır.
    /// </summary>
    Task<IReadOnlyList<ChatMessageFeedback>> GetSimilarFeedbacksAsync(
        string userId,
        float[] queryVector,
        double similarityThreshold,
        int maxAgeMonths,
        int maxCandidates,
        CancellationToken ct = default);
}
