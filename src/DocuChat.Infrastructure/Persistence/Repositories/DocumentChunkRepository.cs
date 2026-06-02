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
        // ImageLinks + Image: belge chunk listesi için görsel path'leri DTO mapping'inde gerekli
        return await _set
            .Where(c => c.DocumentId == documentId)
            .Include(c => c.ImageLinks)
                .ThenInclude(il => il.Image)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
    }
}
