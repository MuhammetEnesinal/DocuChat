namespace DocuChat.Application.Abstractions;

public record ChunkResult(string FileName, string Content);

public interface IVectorSearch
{
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        CancellationToken ct = default,
        Guid? preferredDocumentId = null);
}