using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChunkImageRepository : GenericRepository<ChunkImage>, IChunkImageRepository
{
    public ChunkImageRepository(AppDbContext db) : base(db) { }
}
