using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services.Ai.Retrieval;

public interface IVectorSearch
{
    // precomputedQueryVector: hydeText YOKKEN (ham soru embed edilecekken) çağıranın zaten
    // hesapladığı ham-soru embedding'i. Verilirse gereksiz 2. embedding çağrısı atlanır.
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        string? hydeText = null,
        string? bm25Query = null,
        float[]? precomputedQueryVector = null,
        CancellationToken ct = default);
}
