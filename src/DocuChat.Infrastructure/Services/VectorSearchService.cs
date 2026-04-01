using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Abstractions;
using DocuChat.Infrastructure.Persistence;

namespace DocuChat.Infrastructure.Services;

public class VectorSearchService : IVectorSearch
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedder;

    // Cosine distance eşiği (0 = mükemmel, 2 = tamamen zıt)
    // 0.75 altı = alakalı; çok düşük tutmak sonuç sayısını azaltır
    private const double Threshold = 0.75;

    public VectorSearchService(AppDbContext db, IEmbeddingService embedder)
    {
        _db = db;
        _embedder = embedder;
    }

    public async Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        int topK = 10,
        CancellationToken ct = default)
    {
        var queryVec = await _embedder.GetEmbeddingAsync(question, ct);
        var vector = new Pgvector.Vector(queryVec);

        // Tüm chunk'larda ara (documentId filtresi YOK)
        // Document tablosunu join'le — dosya adını almak için
        var results = await _db.DocumentChunks
            .Where(c => c.Embedding!.CosineDistance(vector) < Threshold)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(topK)
            .Join(_db.Documents,
                  chunk => chunk.DocumentId,
                  doc => doc.Id,
                  (chunk, doc) => new ChunkResult(doc.FileName, chunk.Content))
            .ToListAsync(ct);

        // Eşik altında sonuç yoksa threshold kaldır, en iyi topK'yı getir
        if (results.Count == 0)
        {
            results = await _db.DocumentChunks
                .OrderBy(c => c.Embedding!.CosineDistance(vector))
                .Take(topK)
                .Join(_db.Documents,
                      chunk => chunk.DocumentId,
                      doc => doc.Id,
                      (chunk, doc) => new ChunkResult(doc.FileName, chunk.Content))
                .ToListAsync(ct);
        }

        return results;
    }
}