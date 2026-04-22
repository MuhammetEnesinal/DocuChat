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
    private const int TopChunksPerDoc = 5;
    private const int TopChunksPerDocMulti = 3;
    private const double MultiDocAbsoluteThreshold = 0.45;
    private const double MultiDocRelativeMargin = 0.08;

    public VectorSearchService(AppDbContext db, IEmbeddingService embedder)
    {
        _db = db;
        _embedder = embedder;
    }

    public async Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        CancellationToken ct = default,
        Guid? preferredDocumentId = null)
    {
        var queryVec = await _embedder.GetEmbeddingAsync(question, ct);
        var vector = new Pgvector.Vector(queryVec);

        // 1. En iyi eşleşen chunk'ı bul
        var bestMatch = await FindBestDocument(vector, SimilarityThreshold, ct);
        if (bestMatch == null)
            bestMatch = await FindBestDocument(vector, FallbackThreshold, ct);

        if (bestMatch == null)
            return Array.Empty<ChunkResult>();

        // 2. Önceki belgeyle bağlam koruması
        var primaryDocId = bestMatch.Id;
        if (preferredDocumentId.HasValue && preferredDocumentId.Value != bestMatch.Id)
        {
            var preferredBestChunk = await _db.DocumentChunks
                .Where(c => c.DocumentId == preferredDocumentId.Value)
                .OrderBy(c => c.Embedding!.CosineDistance(vector))
                .Select(c => new { Distance = c.Embedding!.CosineDistance(vector) })
                .FirstOrDefaultAsync(ct);

            if (preferredBestChunk != null && preferredBestChunk.Distance <= bestMatch.Distance * 1.2)
                primaryDocId = preferredDocumentId.Value;
        }

        // 3. Çok belgeli soru tespiti:
        //    Ek belge hem soruyla gerçekten ilgili (mutlak eşik) hem de
        //    primary'ye yakın (göreli margin) olmalı — ikisi birden sağlanmalı
        var maxExtraDistance = bestMatch.Distance + MultiDocRelativeMargin;

        var nearbyDocs = await _db.DocumentChunks
            .Where(c => c.DocumentId != primaryDocId
                        && c.Embedding!.CosineDistance(vector) < MultiDocAbsoluteThreshold
                        && c.Embedding!.CosineDistance(vector) <= maxExtraDistance)
            .GroupBy(c => c.DocumentId)
            .Select(g => new
            {
                DocId = g.Key,
                BestDistance = g.Min(c => c.Embedding!.CosineDistance(vector))
            })
            .OrderBy(g => g.BestDistance)
            .Take(2)
            .Select(g => g.DocId)
            .ToListAsync(ct);

        var results = new List<ChunkResult>();

        // Ana belgeden chunk'lar
        var primaryChunks = await GetTopChunks(primaryDocId, vector,
            nearbyDocs.Count > 0 ? TopChunksPerDocMulti : TopChunksPerDoc, ct);
        results.AddRange(primaryChunks);

        // Ek belgelerden chunk'lar (token bütçesi varsa)
        foreach (var docId in nearbyDocs)
        {
            var extraChunks = await GetTopChunks(docId, vector, TopChunksPerDocMulti, ct);
            results.AddRange(extraChunks);
        }

        return results;
    }

    private async Task<List<ChunkResult>> GetTopChunks(
        Guid docId, Pgvector.Vector vector, int topK, CancellationToken ct)
    {
        // Belgede kaç chunk var?
        var totalChunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .CountAsync(ct);

        // Az chunk'lı belgede (≤10) tüm chunk'ları al — KKE gibi belgeler için
        // Çok chunk'lı belgede sadece en iyi topK tanesini al
        var effectiveTopK = totalChunks <= 10 ? totalChunks : topK;

        return await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(effectiveTopK)
            .OrderBy(c => c.ChunkIndex)
            .Join(_db.Documents,
                  chunk => chunk.DocumentId,
                  doc => doc.Id,
                  (chunk, doc) => new ChunkResult(doc.FileName, chunk.Content))
            .ToListAsync(ct);
    }

    private async Task<DocumentMatch?> FindBestDocument(
        Pgvector.Vector vector, double threshold, CancellationToken ct)
    {
        return await _db.DocumentChunks
            .Where(c => c.Embedding!.CosineDistance(vector) < threshold)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(1)
            .Join(_db.Documents,
                  chunk => chunk.DocumentId,
                  doc => doc.Id,
                  (chunk, doc) => new DocumentMatch(
                      doc.Id, doc.FileName,
                      chunk.Embedding!.CosineDistance(vector)))
            .FirstOrDefaultAsync(ct);
    }

    private record DocumentMatch(Guid Id, string FileName, double Distance);
}