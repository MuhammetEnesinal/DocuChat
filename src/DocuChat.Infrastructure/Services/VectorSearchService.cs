using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Abstractions;
using DocuChat.Infrastructure.Persistence;

namespace DocuChat.Infrastructure.Services;

public class VectorSearchService : IVectorSearch
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedder;

    public VectorSearchService(AppDbContext db, IEmbeddingService embedder)
    {
        _db = db;
        _embedder = embedder;
    }

    public async Task<IReadOnlyList<string>> SearchAsync(
        Guid documentId, string question, int topK = 5, CancellationToken ct = default)
    {
        var queryVec = await _embedder.GetEmbeddingAsync(question, ct);
        var vector = new Pgvector.Vector(queryVec);

        var results = await _db.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(topK)
            .Select(c => c.Content)
            .ToListAsync(ct);

        return results;
    }
}