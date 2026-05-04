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

        return await query
            .OrderByDescending(q => 1 - q.QuestionVector.CosineDistance(vector))
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(QuestionCache entry, CancellationToken ct = default)
    {
        _db.QuestionCaches.Add(entry);
        await _db.SaveChangesAsync(ct);
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
}