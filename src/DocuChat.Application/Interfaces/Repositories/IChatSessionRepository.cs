using DocuChat.Application.Common;
using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IChatSessionRepository : IRepository<ChatSession>
{
    Task<IReadOnlyList<ChatSession>> GetByUserIdAsync(string userId, CancellationToken ct = default);

    Task<PaginatedResult<ChatSession>> GetByUserIdPagedAsync(
        string userId, int page, int pageSize, CancellationToken ct = default);

    Task<PaginatedResult<ChatSession>> GetByUserIdFilteredAsync(
        string userId,
        int page,
        int pageSize,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string sortBy = "createdAt",
        bool ascending = false,
        CancellationToken ct = default);

    Task<ChatSession?> GetWithMessagesAsync(Guid sessionId, CancellationToken ct = default);

    Task<ChatSession?> GetWithMessagesPagedAsync(
        Guid sessionId, int page, int pageSize, CancellationToken ct = default);
}
