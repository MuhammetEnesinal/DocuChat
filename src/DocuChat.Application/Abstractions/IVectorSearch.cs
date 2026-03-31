namespace DocuChat.Application.Abstractions;

public interface IVectorSearch
{
    Task<IReadOnlyList<string>> SearchAsync(Guid documentId, string question, int topK = 5, CancellationToken ct = default);
}