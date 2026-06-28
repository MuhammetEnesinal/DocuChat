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

    /// <summary>
    /// Kullanıcının soru-benzerliği yüksek (≥ threshold) feedback'lerinde net skor:
    ///   dislike_count - like_count
    /// Cache HIT karar mantığı:
    ///   net > 0  → dislike baskın → cache bypass (fresh cevap)
    ///   net < 0  → like baskın    → validate atla, FAST cevap (kullanıcı zaten beğenmiş)
    ///   net = 0  → nötr           → mevcut davranış (sim'e göre FAST veya validate)
    /// </summary>
    Task<int> GetSimilarFeedbackNetAsync(
        string userId,
        float[] queryVector,
        double similarityThreshold,
        CancellationToken ct = default);
}
