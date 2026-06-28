using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services;

public interface IVectorSearch
{
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        CancellationToken ct = default,
        string? hydeText = null,
        string? bm25Query = null);
}
