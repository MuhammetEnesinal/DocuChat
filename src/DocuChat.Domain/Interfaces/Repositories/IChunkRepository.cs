using DocuChat.Domain.Entities;

namespace DocuChat.Domain.Interfaces.Repositories;

public interface IChunkRepository : IRepository<DocumentChunk>
{
    Task<IReadOnlyList<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
    Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
}