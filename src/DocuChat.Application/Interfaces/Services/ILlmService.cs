namespace DocuChat.Application.Interfaces.Services;

public interface ILlmService
{
    Task<string> AskAsync(
        string question,
        IEnumerable<ChunkResult> contextChunks,
        IEnumerable<(string Role, string Content)>? history = null,
        CancellationToken ct = default);

    Task<List<string>> DetectRelevantDocumentsAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        IEnumerable<string> availableDocuments,
        CancellationToken ct = default);
}