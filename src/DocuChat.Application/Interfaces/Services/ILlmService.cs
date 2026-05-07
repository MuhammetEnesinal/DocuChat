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

    Task<bool> IsCacheableAsync(
        string question,
        IEnumerable<(string Role, string Content)>? history = null,
        CancellationToken ct = default);

    /// <summary>Soruyu belge araması için optimize et: kısaltma açma, yazım düzeltme, zamir netleştirme.</summary>
    Task<string> RewriteQueryAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        CancellationToken ct = default);

    /// <summary>Soruyu cevaplayan varsayımsal belge metni üret (HyDE için).</summary>
    Task<string> GenerateHypotheticalDocumentAsync(
        string question,
        CancellationToken ct = default);

    /// <summary>Chunk'ları relevans sırasına göre LLM ile rerank et. 0-indexed sıra döner.</summary>
    Task<IReadOnlyList<int>> RerankChunksAsync(
        string question,
        IReadOnlyList<string> chunkContents,
        int topK,
        CancellationToken ct = default);
}