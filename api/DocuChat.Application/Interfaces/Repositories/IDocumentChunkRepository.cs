using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IDocumentChunkRepository : IRepository<DocumentChunk>
{
    /// Belirli bir belgenin tüm chunk'larını ChunkIndex sırasıyla döner.
    Task<IReadOnlyList<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
}
