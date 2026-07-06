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

namespace DocuChat.Application.Interfaces.Repositories.Chat;

public interface IChatMessageFeedbackRepository : IRepository<ChatMessageFeedback>
{
    // Belirli user-message kombinasyonu için feedback var mı? (UNIQUE check)
    Task<bool> ExistsByUserAndMessageAsync(string userId, Guid messageId, CancellationToken ct = default);

    // Kullanıcının soru benzerliği eşleşen TÜM feedback'lerini (like + dislike) getirir.
    // Clustering ve net dislike count C# katmanında yapılır.
    Task<IReadOnlyList<ChatMessageFeedback>> GetSimilarFeedbacksAsync(
        string userId,
        float[] queryVector,
        double similarityThreshold,
        int maxAgeMonths,
        int maxCandidates,
        CancellationToken ct = default);

    // Kullanıcının soru-benzerliği yüksek (≥ threshold) feedback'lerinde net skor döner
    // (dislike_count - like_count). Cache HIT kararında kullanılır:
    // net > 0  → dislike baskın → cache atlanır, taze cevap üretilir
    // net < 0  → like baskın    → doğrulama atlanır, hızlı cevap döner
    // net = 0  → nötr           → benzerliğe göre hızlı veya doğrulamalı cevap
    Task<int> GetSimilarFeedbackNetAsync(
        string userId,
        float[] queryVector,
        double similarityThreshold,
        CancellationToken ct = default);
}
