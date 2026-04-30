namespace DocuChat.Application.Interfaces.Services;

public record ChunkResult(string FileName, string Content, string? ImagePath = null);
public record ParsedChunk(string Content, string? ImagePath = null);

public interface IVectorSearch
{
    Task<IReadOnlyList<ChunkResult>> SearchAsync(
        string question,
        CancellationToken ct = default,
        Guid? preferredDocumentId = null,
        List<Guid>? relevantDocumentIds = null);
}