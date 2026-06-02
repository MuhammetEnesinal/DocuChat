
using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public IDocumentRepository Documents { get; }
    public IDocumentChunkRepository Chunks { get; }
    public IDocumentImageRepository Images { get; }
    public IChunkImageRepository ChunkImages { get; }
    public IChatSessionRepository Sessions { get; }
    public IChatMessageRepository Messages { get; }
    public IQuestionCacheRepository QuestionCache { get; }

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
        Documents = new DocumentRepository(db);
        Chunks = new DocumentChunkRepository(db);
        Images = new DocumentImageRepository(db);
        ChunkImages = new ChunkImageRepository(db);
        Sessions = new ChatSessionRepository(db);
        Messages = new ChatMessageRepository(db);
        QuestionCache = new QuestionCacheRepository(db);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
