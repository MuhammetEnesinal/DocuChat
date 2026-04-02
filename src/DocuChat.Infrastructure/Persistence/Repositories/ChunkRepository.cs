using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChunkRepository : GenericRepository<DocumentChunk>, IChunkRepository
{
    public ChunkRepository(AppDbContext db) : base(db) { }
}