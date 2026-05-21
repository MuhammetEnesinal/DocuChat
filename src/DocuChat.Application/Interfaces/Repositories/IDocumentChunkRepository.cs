using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

/// <summary>
/// DocumentChunk'a özgü query'ler.
/// FindAsync(c => c.DocumentId == ...) 4 yerde tekrarlanmasın diye özel repo.
/// </summary>
public interface IDocumentChunkRepository : IRepository<DocumentChunk>
{
    /// Belirli bir belgenin tüm chunk'larını ChunkIndex sırasıyla döner.
    Task<IReadOnlyList<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
}
