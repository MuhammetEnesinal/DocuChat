using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services;

public interface IVectorSearch
{
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        string? hydeText = null,
        string? bm25Query = null,
        CancellationToken ct = default);
}
