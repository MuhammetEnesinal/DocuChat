using Microsoft.Extensions.Logging;
using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Repositories.Chat;
using DocuChat.Infrastructure.Persistence.Repositories.Documents;
using DocuChat.Infrastructure.Persistence.Repositories.Caching;
using DocuChat.Application.Interfaces.Repositories.Common;
using DocuChat.Application.Interfaces.Repositories.Chat;
using DocuChat.Application.Interfaces.Repositories.Documents;
using DocuChat.Application.Interfaces.Repositories.Caching;

namespace DocuChat.Infrastructure.Persistence.Repositories.Common;

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
