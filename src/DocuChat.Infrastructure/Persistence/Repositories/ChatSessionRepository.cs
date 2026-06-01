using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Common;
using DocuChat.Application.Common.Specifications;
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

    public async Task<PaginatedResult<ChatSession>> ListAsync(
        ChatSessionFilterSpec spec, CancellationToken ct = default)
    {
        var query = _set.Where(s => s.UserId == spec.UserId);

        if (spec.DateFrom.HasValue)
            query = query.Where(s => s.CreatedAt >= spec.DateFrom.Value);

        if (spec.DateTo.HasValue)
            query = query.Where(s => s.CreatedAt <= spec.DateTo.Value.AddDays(1));

        IOrderedQueryable<ChatSession> ordered = (spec.SortBy, spec.Ascending) switch
        {
            (ChatSessionSortBy.Title, true)  => query.OrderBy(s => s.Title),
            (ChatSessionSortBy.Title, false) => query.OrderByDescending(s => s.Title),
            (_, true)                        => query.OrderBy(s => s.CreatedAt),
            (_, false)                       => query.OrderByDescending(s => s.CreatedAt),
        };

        var total = await ordered.CountAsync(ct);
        var items = await ordered.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize).ToListAsync(ct);
        return new PaginatedResult<ChatSession>(items, total, spec.Page, spec.PageSize);
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
