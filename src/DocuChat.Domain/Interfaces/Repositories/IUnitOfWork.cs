using DocuChat.Domain.Entities;

namespace DocuChat.Domain.Interfaces.Repositories;

public interface IUnitOfWork
{
    IDocumentRepository Documents { get; }
    IChunkRepository Chunks { get; }
    IChatSessionRepository Sessions { get; }
    IRepository<ChatMessage> Messages { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}