// DocuChat.Infrastructure/Persistence/Repositories/QuestionCacheRepository.cs
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;
using DocuChat.Infrastructure.Persistence;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class QuestionCacheRepository : IQuestionCacheRepository
{
    private readonly AppDbContext _db;

    public QuestionCacheRepository(AppDbContext db) => _db = db;

    public async Task<QuestionCache?> FindSimilarAsync(
        float[] queryVector,
        double threshold,
        string? documentIds = null,
        CancellationToken ct = default)
    {
        var vector = new Vector(queryVector);

        var query = _db.QuestionCaches
            .Where(q => 1 - q.QuestionVector.CosineDistance(vector) >= threshold);

        if (!string.IsNullOrWhiteSpace(documentIds))
            query = query.Where(q => q.DocumentIds == documentIds);
        else
            query = query.Where(q => q.DocumentIds == null);

        return await query
            .OrderByDescending(q => 1 - q.QuestionVector.CosineDistance(vector))
            .FirstOrDefaultAsync(ct);
    }

    public Task AddAsync(QuestionCache entry, CancellationToken ct = default)
    {
        _db.QuestionCaches.Add(entry);
        return Task.CompletedTask;
    }

    public async Task IncrementHitAsync(Guid id, CancellationToken ct = default)
    {
        await _db.QuestionCaches
            .Where(q => q.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.HitCount, q => q.HitCount + 1)
                .SetProperty(q => q.LastHitAt, _ => DateTime.UtcNow),
                ct);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _db.QuestionCaches.ExecuteDeleteAsync(ct);
    }

    public async Task ClearByDocumentIdAsync(Guid docId, CancellationToken ct = default)
    {
        var idStr = docId.ToString();
        await _db.QuestionCaches
            .Where(q => q.DocumentIds != null && q.DocumentIds.Contains(idStr))
            .ExecuteDeleteAsync(ct);
    }

    public async Task ClearByDocumentIdsAsync(IEnumerable<Guid> docIds, CancellationToken ct = default)
    {
        var idStrings = docIds.Select(id => id.ToString()).ToList();
        if (idStrings.Count == 0) return;
        await _db.QuestionCaches
            .Where(q => q.DocumentIds != null && idStrings.Any(s => q.DocumentIds.Contains(s)))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetTopByHitCountAsync(int limit, CancellationToken ct = default)
        => await _db.QuestionCaches
            .OrderByDescending(q => q.HitCount)
            .Take(limit)
            .Select(q => q.QuestionText)
            .ToListAsync(ct);

    public async Task<int> DeleteExpiredAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        return await _db.QuestionCaches
            .Where(q => q.HitCount == 0 && q.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
