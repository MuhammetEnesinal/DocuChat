
using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IUnitOfWork
{
    IDocumentRepository Documents { get; }
    IDocumentChunkRepository Chunks { get; }
    IChatSessionRepository Sessions { get; }
    IChatMessageRepository Messages { get; }
    IQuestionCacheRepository QuestionCache { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
