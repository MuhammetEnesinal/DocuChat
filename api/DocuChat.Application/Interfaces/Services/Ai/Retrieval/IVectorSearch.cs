using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services.Ai.Retrieval;

public interface IVectorSearch
{
    // precomputedQueryVector: hydeText YOKKEN (ham soru embed edilecekken) çağıranın zaten
    // hesapladığı ham-soru embedding'i. Verilirse gereksiz 2. embedding çağrısı atlanır.
    // departmentIds: departman izolasyonu. null = filtre yok (admin/global); doluysa yalnız o
    // departmanların belgelerinde aranır; BOŞ liste = hiçbir sonuç (kesin izolasyon).
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        string? hydeText = null,
        string? bm25Query = null,
        float[]? precomputedQueryVector = null,
        IReadOnlyList<Guid>? departmentIds = null,
        CancellationToken ct = default);
}
