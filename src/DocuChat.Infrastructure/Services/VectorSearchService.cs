using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Abstractions;
using DocuChat.Infrastructure.Persistence;

namespace DocuChat.Infrastructure.Services;

public class VectorSearchService : IVectorSearch
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedder;

    private const double SimilarityThreshold = 0.55;
    private const double FallbackThreshold = 0.70;

    public VectorSearchService(AppDbContext db, IEmbeddingService embedder)
    {
        _db = db;
        _embedder = embedder;
    }

    public async Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        int topK = 5,
        CancellationToken ct = default)
    {
        var queryVec = await _embedder.GetEmbeddingAsync(question, ct);
        var vector = new Pgvector.Vector(queryVec);

        var results = await GetChunks(vector, SimilarityThreshold, topK, ct);

        if (results.Count < 2)
            results = await GetChunks(vector, FallbackThreshold, topK, ct);

        if (results.Count == 0)
            return Array.Empty<ChunkResult>();

        // Eşleşen belgeden tüm chunk'ları getir (sayfalararası bağlantı için)
        var matchedDocIds = results.Select(r => r.DocumentId).Distinct().ToList();

        if (matchedDocIds.Count == 1)
        {
            var allChunks = await _db.DocumentChunks
                .Where(c => matchedDocIds.Contains(c.DocumentId))
                .OrderBy(c => c.Embedding!.CosineDistance(vector))
                .Take(topK * 2)
                .Join(_db.Documents,
                      chunk => chunk.DocumentId,
                      doc => doc.Id,
                      (chunk, doc) => new ChunkResultInternal(doc.Id, doc.FileName, chunk.Content))
                .ToListAsync(ct);

            return allChunks.Select(r => new ChunkResult(r.FileName, r.Content)).ToList();
        }

        return results.Select(r => new ChunkResult(r.FileName, r.Content)).ToList();
    }

    private async Task<List<ChunkResultInternal>> GetChunks(
        Pgvector.Vector vector, double threshold, int topK, CancellationToken ct)
    {
        return await _db.DocumentChunks
            .Where(c => c.Embedding!.CosineDistance(vector) < threshold)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(topK)
            .Join(_db.Documents,
                  chunk => chunk.DocumentId,
                  doc => doc.Id,
                  (chunk, doc) => new ChunkResultInternal(doc.Id, doc.FileName, chunk.Content))
            .ToListAsync(ct);
    }

    private record ChunkResultInternal(Guid DocumentId, string FileName, string Content);
}