using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services.Ai.Retrieval;

// Chunk retrieval orkestrasyonu: enriched query + boost + dense/BM25 search + rerank.
// ChatUseCase'i pipeline detaylarından soyutlar.
public interface IRetrievalPipeline
{
    // history: bağlam — enrichment için kullanılır
    // isStandalone: soru history'den BAĞIMSIZ mı? (LLM IsCacheable kararı — true ise boost atlanır,
    //   aksi halde önceki konunun snippet'i embedding'e zehirleyici karışır)
    // useBoost: self-correct retry için kapatılabilir (farklı chunk seti şansı)
    // precomputedQueryVector: ham sorunun (cache için hesaplanmış) embedding'i; boost YOKKEN
    //   gereksiz 2. embedding çağrısını atlamak için VectorSearch'e iletilir.
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        IReadOnlyList<(string Role, string Content)> history,
        bool isStandalone = false,
        bool useBoost = true,
        float[]? precomputedQueryVector = null,
        CancellationToken ct = default);
}
