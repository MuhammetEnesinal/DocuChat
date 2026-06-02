using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IChunkImageRepository : IRepository<ChunkImage>
{
    Task<IReadOnlyList<ChunkImage>> GetByChunkIdAsync(Guid chunkId, CancellationToken ct = default);
}
