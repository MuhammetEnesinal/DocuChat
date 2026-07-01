using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.ServiceContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services.Ai.Reranker;

public class BgeRerankerService : IRerankerService
{
    private readonly HttpClient _http;
    private readonly ILogger<BgeRerankerService> _logger;
    private readonly double _minScore;

    public BgeRerankerService(
        HttpClient http,
        IConfiguration cfg,
        ILogger<BgeRerankerService> logger)
    {
        _http = http;
        _logger = logger;
        _minScore = cfg.GetValue<double>("Reranker:MinScore", 0.0);
    }

    public async Task<IReadOnlyList<RerankedItem>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken ct = default)
    {
        if (documents.Count == 0) return Array.Empty<RerankedItem>();
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("[Reranker] Boş query — vector sırası korunur");
            return FallbackOrder(documents.Count, topN);
        }

        // Boş/whitespace document'ları sidecar'a gönderme — model nötr 0 döner
        var indexed = documents
            .Select((doc, idx) => new { doc = doc ?? string.Empty, idx })
            .Where(x => !string.IsNullOrWhiteSpace(x.doc))
            .ToList();

        if (indexed.Count == 0)
        {
            _logger.LogWarning("[Reranker] Tüm document'lar boş — vector sırası korunur");
            return FallbackOrder(documents.Count, topN);
        }

        // Cross-encoder 512 token limitine sığacak şekilde kısalt
        // 1500 char ≈ 380 token — limit içinde, chunk'ın ~%75'i reranker'a gider
        // (eski 800 char ≈ 200 token → sadece chunk başı görünüyordu → recall kaybı)
        var trimmedDocs = indexed.Select(x => Truncate(x.doc, 1500)).ToList();
        var payload = new RerankRequest(query, trimmedDocs, topN);

        try
        {
            using var resp = await _http.PostAsJsonAsync("/rerank", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[Reranker] HTTP {S}: {B} — vector sırası korunur",
                    (int)resp.StatusCode, body.Length > 300 ? body[..300] : body);
                return FallbackOrder(documents.Count, topN);
            }

            var result = await resp.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken: ct);
            if (result?.Results is null || result.Results.Count == 0)
            {
                _logger.LogWarning("[Reranker] Sidecar boş sonuç döndü — fail-open");
                return FallbackOrder(documents.Count, topN);
            }

            // Sidecar index'i indexed list'in index'i — orijinal documents index'ine map et
            var filtered = result.Results
                .Where(r => r.Score >= _minScore && r.Index < indexed.Count)
                .Select(r => new RerankedItem(indexed[r.Index].idx, r.Score))
                .ToList();

            var scoresStr = string.Join(", ", result.Results.Take(5).Select(r => $"{r.Score:F3}"));
            _logger.LogInformation(
                "[Reranker] {In} aday → {Out} sonuç (min skor {Min}), top 5 skor: [{Scores}]",
                documents.Count, filtered.Count, _minScore, scoresStr);

            // Soft fallback: hiçbir aday eşiği geçmediyse en iyi top-3'ü yine de LLM'e ver.
            // LLM [NO_ANSWER] protokolü ile alakasızı reddeder; AnswerQuality + cache write
            // koşulları yanlış cevabın kalıcılaşmasını engeller. Tamamen boş dönmek yerine
            // bağlamsal/dolaylı eşleşmelere şans tanır (örn. takip sorusunda zayıf reranker skoru).
            if (filtered.Count == 0)
            {
                // En az 0.05 semantik signal olan adayları al; tam 0 skorlu chunk LLM'i
                // [NO_ANSWER] + self-correct döngüsüne sokar — gereksiz main çağrısı maliyeti.
                var soft = result.Results
                    .Where(r => r.Index < indexed.Count && r.Score > 0.05)
                    .OrderByDescending(r => r.Score)
                    .Take(3)
                    .Select(r => new RerankedItem(indexed[r.Index].idx, r.Score))
                    .ToList();
                if (soft.Count > 0)
                {
                    _logger.LogInformation(
                        "[Reranker] Eşik altı — en iyi {N} aday LLM'e geçti (soft fallback, LLM reddedebilir)",
                        soft.Count);
                    return soft;
                }
            }
            return filtered;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Reranker] Servis hatası — vector sırası korunur (fail-open)");
            return FallbackOrder(documents.Count, topN);
        }
    }

    private static string Truncate(string s, int maxChars) =>
        s.Length <= maxChars ? s : s[..maxChars];

    private static IReadOnlyList<RerankedItem> FallbackOrder(int count, int topN)
    {
        var take = Math.Min(count, topN);
        var list = new List<RerankedItem>(take);
        for (var i = 0; i < take; i++) list.Add(new RerankedItem(i, 0.0));
        return list;
    }

    private class RerankRequest
    {
        [JsonPropertyName("query")] public string Query { get; set; }
        [JsonPropertyName("documents")] public List<string> Documents { get; set; }
        [JsonPropertyName("top_n")] public int TopN { get; set; }

        public RerankRequest(string Query, List<string> Documents, int TopN)
        {
            this.Query = Query;
            this.Documents = Documents;
            this.TopN = TopN;
        }
    }

    private class RerankResponse
    {
        [JsonPropertyName("results")] public List<RerankResultDto> Results { get; set; }

        public RerankResponse(List<RerankResultDto> Results)
        {
            this.Results = Results;
        }
    }

    private class RerankResultDto
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("score")] public double Score { get; set; }

        public RerankResultDto(int Index, double Score)
        {
            this.Index = Index;
            this.Score = Score;
        }
    }
}
