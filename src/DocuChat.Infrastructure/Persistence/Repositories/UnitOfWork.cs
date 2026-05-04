
using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
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

    public async Task<IReadOnlyList<(Guid Id, string FileName)>> GetDocumentNamesAsync(
        CancellationToken ct = default)
        => await _db.Documents
            .Select(d => new { d.Id, d.FileName })
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<(Guid, string)>)
                t.Result.Select(x => (x.Id, x.FileName)).ToList(), ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}