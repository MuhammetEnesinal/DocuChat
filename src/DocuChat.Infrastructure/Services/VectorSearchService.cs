using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Abstractions;
using DocuChat.Infrastructure.Persistence;

namespace DocuChat.Infrastructure.Services;

public class VectorSearchService : IVectorSearch
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedder;

    // Vektör arama eşikleri
    private const double SimilarityThreshold = 0.50;
    private const double FallbackThreshold = 0.65;
    private const int TopChunksPerDoc = 5;
    private const int TopChunksPerDocMulti = 3;
    private const double MultiDocAbsoluteThreshold = 0.45;
    private const double MultiDocRelativeMargin = 0.08;

    // Hibrit arama ağırlıkları (toplam 1.0)
    private const double VectorWeight = 0.70;  // Anlamsal benzerlik
    private const double KeywordWeight = 0.30;  // Keyword eşleşmesi

    // Reranking: ilk N chunk'ı getir, en iyi K'yı döndür
    private const int RerankCandidates = 15;

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

        // 1. Primary belgeyi bul
        var bestMatch = await FindBestDocument(vector, SimilarityThreshold, ct)
                     ?? await FindBestDocument(vector, FallbackThreshold, ct);

        if (bestMatch == null) return Array.Empty<ChunkResult>();

        // 2. Bağlam koruması — önceki belgeden devam
        var primaryDocId = bestMatch.Id;
        if (preferredDocumentId.HasValue && preferredDocumentId.Value != bestMatch.Id)
        {
            var preferredBest = await _db.DocumentChunks
                .Where(c => c.DocumentId == preferredDocumentId.Value)
                .OrderBy(c => c.Embedding!.CosineDistance(vector))
                .Select(c => new { Distance = c.Embedding!.CosineDistance(vector) })
                .FirstOrDefaultAsync(ct);

            if (preferredBest != null && preferredBest.Distance <= bestMatch.Distance * 1.2)
                primaryDocId = preferredDocumentId.Value;
        }

        // 3. Ek belgeler (çapraz sorgulama)
        var maxExtraDistance = bestMatch.Distance + MultiDocRelativeMargin;
        var nearbyDocs = await _db.DocumentChunks
            .Where(c => c.DocumentId != primaryDocId
                     && c.Embedding!.CosineDistance(vector) < MultiDocAbsoluteThreshold
                     && c.Embedding!.CosineDistance(vector) <= maxExtraDistance)
            .GroupBy(c => c.DocumentId)
            .Select(g => new { DocId = g.Key, BestDistance = g.Min(c => c.Embedding!.CosineDistance(vector)) })
            .OrderBy(g => g.BestDistance)
            .Take(2)
            .Select(g => g.DocId)
            .ToListAsync(ct);

        var results = new List<ChunkResult>();

        // 4. Hibrit arama + reranking
        var primaryTopK = nearbyDocs.Count > 0 ? TopChunksPerDocMulti : TopChunksPerDoc;
        var primaryChunks = await GetHybridChunks(primaryDocId, vector, question, primaryTopK, ct);
        results.AddRange(primaryChunks);

        foreach (var docId in nearbyDocs)
        {
            var extraChunks = await GetHybridChunks(docId, vector, question, TopChunksPerDocMulti, ct);
            results.AddRange(extraChunks);
        }

        return results;
    }

    // ── Hibrit arama: vektör + keyword skoru birleştir, rerank et ─────────
    private async Task<List<ChunkResult>> GetHybridChunks(
        Guid docId, Pgvector.Vector vector, string question, int topK, CancellationToken ct)
    {
        var totalChunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .CountAsync(ct);

        // Az chunk'lı belgede tüm chunk'ları al
        var candidateK = totalChunks <= 10 ? totalChunks : Math.Min(RerankCandidates, totalChunks);

        // Vektör skoruna göre aday chunk'ları çek
        var candidates = await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(candidateK)
            .Join(_db.Documents,
                  chunk => chunk.DocumentId,
                  doc => doc.Id,
                  (chunk, doc) => new
                  {
                      doc.FileName,
                      chunk.Content,
                      chunk.ChunkIndex,
                      VectorDistance = chunk.Embedding!.CosineDistance(vector)
                  })
            .ToListAsync(ct);

        if (!candidates.Any()) return new List<ChunkResult>();

        // Keyword skorunu hesapla (basit token overlap)
        var queryTokens = Tokenize(question);

        var scored = candidates.Select(c =>
        {
            // Vektör skoru: distance'ı benzerliğe çevir (0-1, yüksek = iyi)
            var vectorScore = 1.0 - c.VectorDistance;

            // Keyword skoru: soru token'larının chunk'ta kaç tanesi geçiyor
            var chunkTokens = Tokenize(c.Content);
            var matchCount = queryTokens.Count(t => chunkTokens.Contains(t));
            var keywordScore = queryTokens.Count > 0
                ? (double)matchCount / queryTokens.Count
                : 0.0;

            // Hibrit skor
            var hybridScore = VectorWeight * vectorScore + KeywordWeight * keywordScore;

            return new { c.FileName, c.Content, c.ChunkIndex, HybridScore = hybridScore };
        })
        .OrderByDescending(x => x.HybridScore)
        .Take(topK)
        .OrderBy(x => x.ChunkIndex)  // Orijinal sıraya göre döndür
        .Select(x => new ChunkResult(x.FileName, x.Content))
        .ToList();

        return scored;
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>();

        return new HashSet<string>(
            text.ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'' },
                       StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2),
            StringComparer.OrdinalIgnoreCase
        );
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