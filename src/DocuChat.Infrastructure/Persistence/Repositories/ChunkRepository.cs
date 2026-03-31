using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChunkRepository : GenericRepository<DocumentChunk>, IChunkRepository
{
    public ChunkRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<DocumentChunk>> GetByDocumentIdAsync(
        Guid documentId, CancellationToken ct = default)
        => await _set.Where(c => c.DocumentId == documentId)
                     .OrderBy(c => c.ChunkIndex)
                     .ToListAsync(ct);

    public async Task DeleteByDocumentIdAsync(
        Guid documentId, CancellationToken ct = default)
    {
        var chunks = await _set
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(ct);
        _set.RemoveRange(chunks);
    }
}