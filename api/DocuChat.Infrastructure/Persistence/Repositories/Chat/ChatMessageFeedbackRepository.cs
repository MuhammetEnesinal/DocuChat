using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Repositories;
using DocuChat.Infrastructure.Persistence.Repositories.Common;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
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

namespace DocuChat.Infrastructure.Persistence.Repositories.Chat;

public class ChatMessageFeedbackRepository
    : GenericRepository<ChatMessageFeedback>, IChatMessageFeedbackRepository
{
    public ChatMessageFeedbackRepository(AppDbContext db) : base(db) { }

    public async Task<bool> ExistsByUserAndMessageAsync(
        string userId, Guid messageId, CancellationToken ct = default)
    {
        return await _set.AnyAsync(
            f => f.UserId == userId && f.MessageId == messageId, ct);
    }

    public async Task<IReadOnlyList<ChatMessageFeedback>> GetSimilarFeedbacksAsync(
        string userId,
        float[] queryVector,
        double similarityThreshold,
        int maxAgeMonths,
        int maxCandidates,
        CancellationToken ct = default)
    {
        if (queryVector.Length == 0) return Array.Empty<ChatMessageFeedback>();

        var cutoff = DateTime.UtcNow.AddMonths(-maxAgeMonths);
        var vec = new Vector(queryVector);
        // cosine distance = 1 - cosine similarity → threshold ters çevrilir
        // similarity > 0.75 demek distance < 0.25
        var maxDistance = 1.0 - similarityThreshold;

        return await _set
            .Where(f => f.UserId == userId)
            .Where(f => f.CreatedAt >= cutoff)
            .Where(f => f.QuestionVector!.CosineDistance(vec) <= maxDistance)
            .OrderBy(f => f.QuestionVector!.CosineDistance(vec))
            .Take(maxCandidates)
            .ToListAsync(ct);
    }

    public async Task<int> GetSimilarFeedbackNetAsync(
        string userId,
        float[] queryVector,
        double similarityThreshold,
        CancellationToken ct = default)
    {
        if (queryVector.Length == 0) return 0;

        var vec = new Vector(queryVector);
        var maxDistance = 1.0 - similarityThreshold;

        // Tek query → sum ile DB tarafında hesapla (round-trip yok).
        // dislike Rating=-1, like Rating=+1 → -Rating toplamı = dislike - like
        return await _set
            .Where(f => f.UserId == userId)
            .Where(f => f.QuestionVector!.CosineDistance(vec) <= maxDistance)
            .SumAsync(f => -f.Rating, ct);
    }
}
