using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Infrastructure.Persistence;

namespace DocuChat.Infrastructure.Services;

public class VectorSearchService : IVectorSearch
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedder;
    private readonly ILogger<VectorSearchService> _logger;

    // Vektör arama eşikleri (appsettings.json VectorSearch bölümünden okunur)
    private readonly double _similarityThreshold;
    private readonly double _fallbackThreshold;
    private readonly int _topChunksPerDoc;
    private readonly int _topChunksPerDocMulti;
    private readonly double _multiDocAbsoluteThreshold;
    private readonly double _multiDocRelativeMargin;
    private readonly double _vectorWeight;
    private readonly double _keywordWeight;
    private readonly int _rerankCandidates;

    public VectorSearchService(AppDbContext db, IEmbeddingService embedder, IConfiguration cfg, ILogger<VectorSearchService> logger)
    {
        _db = db;
        _embedder = embedder;
        _logger = logger;

        _similarityThreshold      = cfg.GetValue<double>("VectorSearch:SimilarityThreshold", 0.50);
        _fallbackThreshold        = cfg.GetValue<double>("VectorSearch:FallbackThreshold", 0.65);
        _multiDocAbsoluteThreshold = cfg.GetValue<double>("VectorSearch:MultiDocAbsoluteThreshold", 0.35);
        _multiDocRelativeMargin   = cfg.GetValue<double>("VectorSearch:MultiDocRelativeMargin", 0.05);
        _vectorWeight             = cfg.GetValue<double>("VectorSearch:VectorWeight", 0.70);
        _keywordWeight            = cfg.GetValue<double>("VectorSearch:KeywordWeight", 0.30);
        _rerankCandidates         = cfg.GetValue<int>("VectorSearch:RerankCandidates", 15);
        _topChunksPerDoc          = cfg.GetValue<int>("VectorSearch:TopChunksPerDoc", 5);
        _topChunksPerDocMulti     = cfg.GetValue<int>("VectorSearch:TopChunksPerDocMulti", 3);
    }

    public async Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        CancellationToken ct = default,
        Guid? preferredDocumentId = null,
        List<Guid>? relevantDocumentIds = null,
        string? hydeText = null)
    {
        // HyDE: varsayımsal metin varsa onu embed et, yoksa soruyu
        var textToEmbed = !string.IsNullOrWhiteSpace(hydeText) ? hydeText : question;
        var queryVec = await _embedder.GetEmbeddingAsync(textToEmbed, ct);
        var vector = new Pgvector.Vector(queryVec);

        // LLM belge tespiti yaptıysa sadece o belgelerden ara
        if (relevantDocumentIds != null && relevantDocumentIds.Any())
        {
            _logger.LogInformation("[VectorSearch] Kısıtlı arama: {DocIds}", string.Join(", ", relevantDocumentIds));
            var isMultiDoc = relevantDocumentIds.Count > 1;
            var results = new List<ChunkResult>();
            foreach (var docId in relevantDocumentIds)
            {
                var docChunks = await GetHybridChunks(docId, vector, question, isMultiDoc, ct);
                results.AddRange(docChunks);
            }
            return results;
        }

        // Normal arama — tüm belgelerden
        var bestMatch = await FindBestDocument(vector, _similarityThreshold, ct)
                     ?? await FindBestDocument(vector, _fallbackThreshold, ct);

        if (bestMatch == null) return Array.Empty<ChunkResult>();

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

        var maxExtraDistance = bestMatch.Distance + _multiDocRelativeMargin;
        var nearbyDocs = await _db.DocumentChunks
            .Where(c => c.DocumentId != primaryDocId
                     && c.Embedding!.CosineDistance(vector) < _multiDocAbsoluteThreshold
                     && c.Embedding!.CosineDistance(vector) <= maxExtraDistance)
            .GroupBy(c => c.DocumentId)
            .Select(g => new { DocId = g.Key, BestDistance = g.Min(c => c.Embedding!.CosineDistance(vector)) })
            .OrderBy(g => g.BestDistance)
            .Take(2)
            .Select(g => g.DocId)
            .ToListAsync(ct);

        var results2 = new List<ChunkResult>();
        var primaryChunks = await GetHybridChunks(primaryDocId, vector, question, isMultiDoc: nearbyDocs.Count > 0, ct);
        results2.AddRange(primaryChunks);

        foreach (var docId in nearbyDocs)
        {
            var extraChunks = await GetHybridChunks(docId, vector, question, isMultiDoc: true, ct);
            results2.AddRange(extraChunks);
        }

        return results2;
    }

    // ── Hibrit arama: vektör + keyword skoru birleştir, rerank et ─────────
    private async Task<List<ChunkResult>> GetHybridChunks(
        Guid docId, Pgvector.Vector vector, string question, bool isMultiDoc, CancellationToken ct)
    {
        var totalChunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == docId)
            .CountAsync(ct);

        // Dynamic TopK — belge büyüklüğüne göre
        var topK = (totalChunks, isMultiDoc) switch
        {
            (<= 10, false) => Math.Min(totalChunks, 5),
            (<= 10, true)  => Math.Min(totalChunks, 4),
            (<= 30, false) => 6,
            (<= 80, false) => 8,
            (_,     false) => 12,
            (<= 30, true)  => 4,
            (_,     true)  => 6,
        };

        _logger.LogInformation("[VectorSearch] DocId={DocId}, totalChunks={Total}, topK={TopK}, isMultiDoc={IsMultiDoc}", docId, totalChunks, topK, isMultiDoc);

        var candidateK = Math.Min(_rerankCandidates, totalChunks);

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
                      chunk.ImagePath,
                      chunk.Header,
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
            var hybridScore = _vectorWeight * vectorScore + _keywordWeight * keywordScore;

            return new { c.FileName, c.Content, c.ChunkIndex, c.ImagePath, c.Header, HybridScore = hybridScore };
        })
        .OrderByDescending(x => x.HybridScore)
        .Take(topK)
        .OrderBy(x => x.ChunkIndex)  // Orijinal sıraya göre döndür
        .Select(x => new ChunkResult(x.FileName, x.Content, x.ImagePath, x.Header))
        .ToList();

        return scored;
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>();

        return new HashSet<string>(
            text.ToLower(new System.Globalization.CultureInfo("tr-TR"))
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