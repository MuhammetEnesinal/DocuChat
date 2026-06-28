using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Common.Results;
using DocuChat.Application.Common.Specifications;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChatSessionRepository : GenericRepository<ChatSession>, IChatSessionRepository
{
    public ChatSessionRepository(AppDbContext db) : base(db) { }

    // Default davranış: arşivlenmemiş session'ları, pinned ÖNCE, sonra tarihe göre.
    // Pinned olanlar arasında PinnedAt'e göre (en son sabitlenmiş en üstte).
    public async Task<IReadOnlyList<ChatSession>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
        => await _set
                 .Where(s => s.UserId == userId && !s.IsArchived)
                 .OrderByDescending(s => s.IsPinned)
                 .ThenByDescending(s => s.PinnedAt)
                 .ThenByDescending(s => s.CreatedAt)
                 .ToListAsync(ct);

    public async Task<PaginatedResult<ChatSession>> GetByUserIdPagedAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _set
            .Where(s => s.UserId == userId && !s.IsArchived)
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.PinnedAt)
            .ThenByDescending(s => s.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PaginatedResult<ChatSession>(items, total, page, pageSize);
    }

    public async Task<PaginatedResult<ChatSession>> ListAsync(
        ChatSessionFilterSpec spec, CancellationToken ct = default)
    {
        var query = _set.Where(s => s.UserId == spec.UserId);

        // Archived filter: true = sadece arşiv, false = sadece aktif, null = hepsi
        if (spec.Archived.HasValue)
            query = query.Where(s => s.IsArchived == spec.Archived.Value);

        if (spec.DateFrom.HasValue)
            query = query.Where(s => s.CreatedAt >= spec.DateFrom.Value);

        if (spec.DateTo.HasValue)
            query = query.Where(s => s.CreatedAt <= spec.DateTo.Value.AddDays(1));

        // Pin önce uygulanır; kullanıcının seçtiği sıralama ondan SONRA gelir.
        // (Pinned olmayan iki session'da kullanıcı sıralaması belirleyici.)
        IOrderedQueryable<ChatSession> pinnedFirst = query
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.PinnedAt);

        IOrderedQueryable<ChatSession> ordered = (spec.SortBy, spec.Ascending) switch
        {
            (ChatSessionSortBy.Title, true)  => pinnedFirst.ThenBy(s => s.Title),
            (ChatSessionSortBy.Title, false) => pinnedFirst.ThenByDescending(s => s.Title),
            (_, true)                        => pinnedFirst.ThenBy(s => s.CreatedAt),
            (_, false)                       => pinnedFirst.ThenByDescending(s => s.CreatedAt),
        };

        var total = await ordered.CountAsync(ct);
        var items = await ordered.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize).ToListAsync(ct);
        return new PaginatedResult<ChatSession>(items, total, spec.Page, spec.PageSize);
    }

    // Bir kullanıcının arşivlenmiş session sayısı — sidebar "Arşiv (N)" badge için.
    public async Task<int> GetArchivedCountAsync(string userId, CancellationToken ct = default)
        => await _set.CountAsync(s => s.UserId == userId && s.IsArchived, ct);

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
