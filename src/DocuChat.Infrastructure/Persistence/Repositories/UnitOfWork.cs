using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;
using DocuChat.Infrastructure.Persistence.Repositories;

namespace DocuChat.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public IDocumentRepository Documents { get; }
    public IChunkRepository Chunks { get; }
    public IChatSessionRepository Sessions { get; }
    public IRepository<ChatMessage> Messages { get; }

    public UnitOfWork(AppDbContext db,
        IDocumentRepository documents,
        IChunkRepository chunks,
        IChatSessionRepository sessions,
        IRepository<ChatMessage> messages)
    {
        _db = db;
        Documents = documents;
        Chunks = chunks;
        Sessions = sessions;
        Messages = messages;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}