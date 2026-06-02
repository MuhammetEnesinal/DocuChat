using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
using Pgvector.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Infrastructure.Persistence;
using DocuChat.Application.ServiceContracts;

namespace DocuChat.Infrastructure.Services.Ai.Retrieval;

public class VectorSearchService : IVectorSearch
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedder;
    private readonly IRerankerService _reranker;
    private readonly ILogger<VectorSearchService> _logger;

    // Vektör arama eşikleri (appsettings.json VectorSearch bölümünden okunur)
    private readonly int _rerankCandidates;
    private readonly bool _rerankerEnabled;
    private readonly bool _bm25Enabled;
    private readonly string _bm25TsConfig;
    private readonly int _rrfK;

    public VectorSearchService(
        AppDbContext db,
        IEmbeddingService embedder,
        IRerankerService reranker,
        IConfiguration cfg,
        ILogger<VectorSearchService> logger)
    {
        _db = db;
        _embedder = embedder;
        _reranker = reranker;
        _logger = logger;

        _rerankCandidates         = cfg.GetValue<int>("VectorSearch:RerankCandidates", 20);
        _rerankerEnabled          = cfg.GetValue<bool>("Reranker:Enabled", true);
        _bm25Enabled              = cfg.GetValue<bool>("VectorSearch:Bm25Enabled", true);
        _bm25TsConfig             = cfg.GetValue<string>("VectorSearch:Bm25TsConfig", "turkish")!;
        _rrfK                     = cfg.GetValue<int>("VectorSearch:RrfK", 60);
    }

    public async Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        CancellationToken ct = default,
        string? hydeText = null,
        string? bm25Query = null)
    {
        var bm25Text = !string.IsNullOrWhiteSpace(bm25Query) ? bm25Query : question;
        var textToEmbed = !string.IsNullOrWhiteSpace(hydeText) ? hydeText : question;
        var queryVec = await _embedder.GetEmbeddingAsync(textToEmbed, ct);
        var vector = new Pgvector.Vector(queryVec);

        // Tüm chunks içinde global dense + BM25 → RRF → global reranker → top K.

        var denseRanked = await QueryDenseGlobalAsync(vector, _rerankCandidates, ct);

        var bm25Ranked = _bm25Enabled
            ? await QueryBm25GlobalAsync(bm25Text, _rerankCandidates, ct)
            : new List<(Guid, int)>();

        if (denseRanked.Count == 0 && bm25Ranked.Count == 0)
        {
            _logger.LogInformation("[VectorSearch] Sonuç yok (dense=0, bm25=0)");
            return Array.Empty<ChunkResult>();
        }

        var fusedIds = bm25Ranked.Count > 0
            ? FuseRrf(denseRanked, bm25Ranked, _rerankCandidates)
            : denseRanked.Select(d => d.Id).ToList();

        // Top RRF aday'larını DB'den çek (chunk + doc metadata + image join'leri)
        // Görsel kaynağı: ChunkImages → DocumentImages join (artık chunk.ImagePath JSON DEĞİL).
        var candidates = await _db.DocumentChunks
            .Where(c => fusedIds.Contains(c.Id))
            .Select(c => new
            {
                c.Id,
                FileName = c.Document!.FileName,
                c.Content,
                c.ChunkIndex,
                c.Header,
                c.PageNumber,
                ImagePaths = c.ImageLinks
                    .OrderBy(il => il.PositionInChunk)
                    .Select(il => il.Image!.Path)
                    .ToList()
            })
            .ToListAsync(ct);

        // RRF sırasını koru
        candidates = fusedIds
            .Select(id => candidates.FirstOrDefault(c => c.Id == id))
            .Where(c => c != null)
            .ToList()!;

        _logger.LogInformation("[VectorSearch] Global Dense={D}, BM25={B} → RRF top {C}",
            denseRanked.Count, bm25Ranked.Count, candidates.Count);

        // LLM'e gönderilecek finaldeki sıra REranker skoruna göre (en alakalı en üstte).
        var topK = Math.Min(candidates.Count, GetDynamicTopK(candidates));
        List<ChunkResult> ordered;

        if (_rerankerEnabled && candidates.Count > 0)
        {
            var docs = candidates.Select(c => c.Content).ToList();
            var reranked = await _reranker.RerankAsync(question, docs, topK, ct);

            // Reranker MinScore filtresi (config: Reranker:MinScore)
            ordered = reranked
                .Select(r => new
                {
                    Chunk = candidates[r.OriginalIndex],
                    r.Score
                })
                .Where(x => x.Score >= 0)  // negative skor = istenmeyen
                .OrderByDescending(x => x.Score)
                .Select(x => new ChunkResult(
                    x.Chunk.FileName,
                    x.Chunk.Content,
                    SerializeImagePaths(x.Chunk.ImagePaths),
                    x.Chunk.Header))
                .ToList();

            // Doc dağılımını logla — debug için
            var docDist = ordered.GroupBy(c => c.FileName)
                .Select(g => $"{g.Key}({g.Count()})")
                .ToList();
            _logger.LogInformation("[VectorSearch] Reranker top {K} — belge dağılımı: {Dist}",
                ordered.Count, string.Join(", ", docDist));
        }
        else
        {
            ordered = candidates
                .Take(topK)
                .Select(c => new ChunkResult(
                    c.FileName, c.Content, SerializeImagePaths(c.ImagePaths), c.Header))
                .ToList();
        }

        return ordered;
    }

    // ChunkResult.ImagePath JSON string formatını korur — LlmService.BuildContextAndImages
    // bu formatı parse edip [IMG:N] markerlarını çözer.
    private static string? SerializeImagePaths(List<string> paths) =>
        paths.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(paths);

    private static int GetDynamicTopK(int candidateCount)
    {
        if (candidateCount <= 5) return candidateCount;
        if (candidateCount <= 10) return 6;
        if (candidateCount <= 20) return 10;
        return 12;
    }

    private static int GetDynamicTopK<T>(IReadOnlyList<T> candidates) => GetDynamicTopK(candidates.Count);

    private async Task<List<(Guid Id, int Rank)>> QueryDenseGlobalAsync(
        Pgvector.Vector vector, int topN, CancellationToken ct)
    {
        var ids = await _db.DocumentChunks
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(topN)
            .Select(c => c.Id)
            .ToListAsync(ct);

        return ids.Select((id, idx) => (id, idx + 1)).ToList();
    }

    // BM25 (PostgreSQL FTS) hata olursa fail-open: dense ile devam.
    private async Task<List<(Guid Id, int Rank)>> QueryBm25GlobalAsync(
        string question, int topN, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question)) return new();

        try
        {
            var ids = await _db.DocumentChunks
                .Where(c => EF.Property<NpgsqlTsVector>(c, "TsVector")
                              .Matches(EF.Functions.WebSearchToTsQuery(_bm25TsConfig, question)))
                .OrderByDescending(c => EF.Property<NpgsqlTsVector>(c, "TsVector")
                              .RankCoverDensity(EF.Functions.WebSearchToTsQuery(_bm25TsConfig, question)))
                .Take(topN)
                .Select(c => c.Id)
                .ToListAsync(ct);

            return ids.Select((id, idx) => (id, idx + 1)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[BM25] Sorgu hatası — dense ile devam: {Msg}", ex.Message);
            return new();
        }
    }

    private List<Guid> FuseRrf(
        IReadOnlyList<(Guid Id, int Rank)> dense,
        IReadOnlyList<(Guid Id, int Rank)> bm25,
        int topN)
    {
        var scores = new Dictionary<Guid, double>();
        foreach (var (id, rank) in dense)
            scores[id] = scores.GetValueOrDefault(id) + 1.0 / (_rrfK + rank);
        foreach (var (id, rank) in bm25)
            scores[id] = scores.GetValueOrDefault(id) + 1.0 / (_rrfK + rank);

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .Select(kv => kv.Key)
            .ToList();
    }

}
