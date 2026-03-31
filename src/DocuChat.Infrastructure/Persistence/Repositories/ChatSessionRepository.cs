using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChatSessionRepository : GenericRepository<ChatSession>, IChatSessionRepository
{
    public ChatSessionRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<ChatSession>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
        => await _set.Include(s => s.Document)
                     .Where(s => s.UserId == userId)
                     .OrderByDescending(s => s.CreatedAt)
                     .ToListAsync(ct);

    public async Task<IReadOnlyList<ChatSession>> GetByDocumentIdAsync(
        Guid documentId, CancellationToken ct = default)
        => await _set.Where(s => s.DocumentId == documentId)
                     .ToListAsync(ct);

    public async Task<ChatSession?> GetWithMessagesAsync(
        Guid sessionId, CancellationToken ct = default)
        => await _set.Include(s => s.Messages)
                     .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
}