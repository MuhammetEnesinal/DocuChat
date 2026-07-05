
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;

using DocuChat.Application.Interfaces.Repositories.Chat;
using DocuChat.Application.Interfaces.Repositories.Documents;
using DocuChat.Application.Interfaces.Repositories.Caching;
namespace DocuChat.Application.Interfaces.Repositories.Common;

public interface IUnitOfWork
{
    IDocumentRepository Documents { get; }
    IDocumentChunkRepository Chunks { get; }
    IDocumentImageRepository Images { get; }
    IChunkImageRepository ChunkImages { get; }
    IChatSessionRepository Sessions { get; }
    IChatMessageRepository Messages { get; }
    IChatMessageFeedbackRepository Feedback { get; }
    IQuestionCacheRepository QuestionCache { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
