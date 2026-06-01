using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services;

// Chunk retrieval orkestrasyonu: HyDE + enriched query + boost + dense/BM25 search.
// ChatUseCase'i pipeline detaylarından soyutlar.
public interface IRetrievalPipeline
{
    // history: bağlam — enrichment için kullanılır
    // precomputedHyde: cache check ile paralel hesaplanmış HyDE (varsa); yoksa pipeline kendi üretir
    // isStandalone: soru history'den BAĞIMSIZ mı? (LLM IsCacheable kararı — true ise boost atlanır,
    //   aksi halde önceki konunun snippet'i embedding'e zehirleyici karışır)
    // useHyde / useBoost: self-correct retry için kapatılabilir (farklı chunk seti şansı)
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        IReadOnlyList<(string Role, string Content)> history,
        string? precomputedHyde = null,
        bool isStandalone = false,
        bool useHyde = true,
        bool useBoost = true,
        CancellationToken ct = default);
}
