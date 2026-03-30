using DocuChat.Domain.Entities;

namespace DocuChat.Domain.Interfaces.Repositories;

public interface IChatSessionRepository : IRepository<ChatSession>
{
    Task<IReadOnlyList<ChatSession>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSession>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
    Task<ChatSession?> GetWithMessagesAsync(Guid sessionId, CancellationToken ct = default);
}