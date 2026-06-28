
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    public IChatMessageFeedbackRepository Feedback { get; }
    public IQuestionCacheRepository QuestionCache { get; }

    public UnitOfWork(
        AppDbContext db,
        ILogger<QuestionCacheRepository> cacheLogger,
        ILogger<ChatMessageRepository> messageLogger)
    {
        _db = db;
        Documents = new DocumentRepository(db);
        Chunks = new DocumentChunkRepository(db);
        Images = new DocumentImageRepository(db);
        ChunkImages = new ChunkImageRepository(db);
        Sessions = new ChatSessionRepository(db);
        Messages = new ChatMessageRepository(db, messageLogger);
        Feedback = new ChatMessageFeedbackRepository(db);
        QuestionCache = new QuestionCacheRepository(db, cacheLogger);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
