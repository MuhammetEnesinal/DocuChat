using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChatSessionRepository : GenericRepository<ChatSession>, IChatSessionRepository
{
    public ChatSessionRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<ChatSession>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
        => await _set
                 .Where(s => s.UserId == userId)
                 .OrderByDescending(s => s.CreatedAt)
                 .ToListAsync(ct);

    public async Task<PaginatedResult<ChatSession>> GetByUserIdPagedAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _set.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PaginatedResult<ChatSession>(items, total, page, pageSize);
    }

    public async Task<PaginatedResult<ChatSession>> GetByUserIdFilteredAsync(
        string userId,
        int page,
        int pageSize,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string sortBy = "createdAt",
        bool ascending = false,
        CancellationToken ct = default)
    {
        var query = _set.Where(s => s.UserId == userId);

        if (dateFrom.HasValue)
            query = query.Where(s => s.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(s => s.CreatedAt <= dateTo.Value.AddDays(1));

        IOrderedQueryable<ChatSession> ordered = sortBy.ToLowerInvariant() switch
        {
            "title" => ascending
                ? query.OrderBy(s => s.Title)
                : query.OrderByDescending(s => s.Title),
            _ => ascending
                ? query.OrderBy(s => s.CreatedAt)
                : query.OrderByDescending(s => s.CreatedAt),
        };

        var total = await ordered.CountAsync(ct);
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PaginatedResult<ChatSession>(items, total, page, pageSize);
    }

    public async Task<ChatSession?> GetWithMessagesAsync(
        Guid sessionId, CancellationToken ct = default)
        => await _set
                 .Include(s => s.Messages)
                 .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task<ChatSession?> GetWithMessagesPagedAsync(
        Guid sessionId, int page, int pageSize, CancellationToken ct = default)
    {
        var session = await _set.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;

        var messages = await _db.Set<ChatMessage>()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        session.Messages = messages;
        return session;
    }
}
