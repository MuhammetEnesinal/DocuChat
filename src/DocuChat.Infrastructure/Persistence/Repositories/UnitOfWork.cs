using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork, IDisposable
{
    private readonly AppDbContext _db;

    public IRepository<Document> Documents { get; }
    public IRepository<DocumentChunk> Chunks { get; }
    public IChatSessionRepository Sessions { get; }
    public IRepository<ChatMessage> Messages { get; }

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
        Documents = new GenericRepository<Document>(db);
        Chunks = new GenericRepository<DocumentChunk>(db);
        Sessions = new ChatSessionRepository(db);
        Messages = new GenericRepository<ChatMessage>(db);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public void Dispose() => _db.Dispose();
}