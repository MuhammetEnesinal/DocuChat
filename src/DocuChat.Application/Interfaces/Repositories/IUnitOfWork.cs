using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IUnitOfWork
{
    IDocumentRepository Documents { get; }
    IChunkRepository Chunks { get; }
    IChatSessionRepository Sessions { get; }
    IRepository<ChatMessage> Messages { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}