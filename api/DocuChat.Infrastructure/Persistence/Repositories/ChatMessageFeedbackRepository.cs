using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

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
}
