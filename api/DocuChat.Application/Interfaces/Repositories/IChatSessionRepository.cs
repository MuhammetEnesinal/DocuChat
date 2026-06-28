using DocuChat.Application.Common.Results;
using DocuChat.Application.Common.Specifications;
using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IChatSessionRepository : IRepository<ChatSession>
{
    Task<IReadOnlyList<ChatSession>> GetByUserIdAsync(string userId, CancellationToken ct = default);

    Task<PaginatedResult<ChatSession>> GetByUserIdPagedAsync(
        string userId, int page, int pageSize, CancellationToken ct = default);

    // Spec ile filtre + sıralama + sayfalama — imza yeni filtre eklendiğinde kırılmaz.
    Task<PaginatedResult<ChatSession>> ListAsync(
        ChatSessionFilterSpec spec, CancellationToken ct = default);

    Task<ChatSession?> GetWithMessagesAsync(Guid sessionId, CancellationToken ct = default);

    Task<ChatSession?> GetWithMessagesPagedAsync(
        Guid sessionId, int page, int pageSize, CancellationToken ct = default);

    // Sidebar'da "Arşiv (N)" badge için kullanıcının arşivlenmiş session sayısı.
    Task<int> GetArchivedCountAsync(string userId, CancellationToken ct = default);
}
