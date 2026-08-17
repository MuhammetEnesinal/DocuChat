using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services.Ai.Retrieval;

// Chunk retrieval orkestrasyonu: enriched query + boost + dense/FTS search + rerank.
// ChatUseCase'i pipeline detaylarından soyutlar.
public interface IRetrievalPipeline
{
    // history: bağlam — enrichment için kullanılır
    // isStandalone: soru history'den BAĞIMSIZ mı? (LLM IsCacheable kararı — true ise boost atlanır,
    //   aksi halde önceki konunun snippet'i embedding'e zehirleyici karışır)
    // useBoost: self-correct retry için kapatılabilir (farklı chunk seti şansı)
    // precomputedQueryVector: ham sorunun (cache için hesaplanmış) embedding'i; boost YOKKEN
    //   gereksiz 2. embedding çağrısını atlamak için VectorSearch'e iletilir.
    // departmentIds: departman izolasyonu. null = filtre yok (admin/global); doluysa yalnız o
    //   departmanların belgelerinde aranır; BOŞ liste = hiçbir sonuç (kesin izolasyon).
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        IReadOnlyList<(string Role, string Content)> history,
        bool isStandalone = false,
        bool useBoost = true,
        float[]? precomputedQueryVector = null,
        IReadOnlyList<Guid>? departmentIds = null,
        CancellationToken ct = default);
}
