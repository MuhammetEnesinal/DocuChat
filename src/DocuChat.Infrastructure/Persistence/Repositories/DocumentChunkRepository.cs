using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class DocumentChunkRepository : GenericRepository<DocumentChunk>, IDocumentChunkRepository
{
    public DocumentChunkRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<DocumentChunk>> GetByDocumentIdAsync(
        Guid documentId, CancellationToken ct = default)
    {
        return await _set
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
    }
}
