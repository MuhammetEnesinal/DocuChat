using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChunkImageRepository : GenericRepository<ChunkImage>, IChunkImageRepository
{
    public ChunkImageRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<ChunkImage>> GetByChunkIdAsync(
        Guid chunkId, CancellationToken ct = default)
    {
        return await _set
            .Where(ci => ci.ChunkId == chunkId)
            .OrderBy(ci => ci.PositionInChunk)
            .ToListAsync(ct);
    }
}
