using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services.Ai.Reranker;

public interface IRerankerService
{
    Task<IReadOnlyList<RerankedItem>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken ct = default);
}
