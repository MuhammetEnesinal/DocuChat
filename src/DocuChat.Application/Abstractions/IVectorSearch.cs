namespace DocuChat.Application.Abstractions;

public record ChunkResult(string FileName, string Content);

public interface IVectorSearch
{
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        int topK = 10,
        CancellationToken ct = default);
}