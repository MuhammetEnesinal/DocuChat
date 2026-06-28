using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.ServiceContracts;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services.Ai.Retrieval;

public sealed class RetrievalPipelineService : IRetrievalPipeline
{
    private readonly IVectorSearch _vectorSearch;
    private readonly ILlmService _llm;
    private readonly ILogger<RetrievalPipelineService> _logger;

    public RetrievalPipelineService(
        IVectorSearch vectorSearch,
        ILlmService llm,
        ILogger<RetrievalPipelineService> logger)
    {
        _vectorSearch = vectorSearch;
        _llm = llm;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        IReadOnlyList<(string Role, string Content)> history,
        bool isStandalone = false,
        bool useBoost = true,
        CancellationToken ct = default)
    {
        // History bazlı enriched query (BM25 için kısa, embedding için zenginleştirilmiş)
        string? enrichedFromLlm = null;
        string? embedBoostText = null;
        if (history.Count > 0)
        {
            try { enrichedFromLlm = await _llm.BuildContextualSearchQueryAsync(question, history, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "[SearchEnrich] Atlandı"); }

            // Boost (önceki asistan cevabını embedding'e ekleme) sadece soru gerçekten
            // history'ye bağımlıysa yapılır. Sinyal: LLM'in IsCacheable kararı (isStandalone).
            // Standalone bir soruda boost embedding'i farklı konuya kaydırır → reranker 0 verir.
            if (useBoost && !isStandalone)
            {
                var lastAssistant = history.LastOrDefault(h =>
                    h.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)).Content;
                if (!string.IsNullOrWhiteSpace(lastAssistant))
                {
                    var snippet = lastAssistant.Length > 600 ? lastAssistant[..600] : lastAssistant;
                    embedBoostText = string.IsNullOrWhiteSpace(enrichedFromLlm)
                        ? snippet
                        : enrichedFromLlm + " " + snippet;
                }
            }
            else if (useBoost && isStandalone)
            {
                _logger.LogInformation("[Boost] Atlandı — soru standalone (IsCacheable=true)");
            }
        }

        // Embedding: boost'lu (varsa) > enriched > ham soru (VectorSearch içinde null ise ham)
        // BM25: kısa tutulmalı; uzun metin PG tsquery stack'i taşırır → enriched (varsa) veya ham soru
        var embedText = embedBoostText ?? enrichedFromLlm;
        var bm25Query = enrichedFromLlm ?? question;

        var chunks = await _vectorSearch.SearchAsync(
            question, hydeText: embedText, bm25Query: bm25Query, ct: ct);

        return chunks.ToList();
    }
}
