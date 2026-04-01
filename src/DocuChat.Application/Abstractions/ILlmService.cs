using DocuChat.Application.Abstractions;

namespace DocuChat.Application.Abstractions;

public interface ILlmService
{
    Task<string> AskAsync(
        string question,
        IEnumerable<ChunkResult> contextChunks,
        CancellationToken ct = default);
}