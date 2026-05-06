
using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IUnitOfWork
{
    IRepository<Document> Documents { get; }
    IRepository<DocumentChunk> Chunks { get; }
    IChatSessionRepository Sessions { get; }
    IRepository<ChatMessage> Messages { get; }
    IQuestionCacheRepository QuestionCache { get; }

    Task<IReadOnlyList<(Guid Id, string FileName)>> GetDocumentNamesAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}