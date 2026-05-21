using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChatMessageRepository : GenericRepository<ChatMessage>, IChatMessageRepository
{
    public ChatMessageRepository(AppDbContext db) : base(db) { }

    public async Task<int> CountBySessionAsync(Guid sessionId, CancellationToken ct = default)
        => await _set.CountAsync(m => m.SessionId == sessionId, ct);

    public async Task<IReadOnlyList<ChatMessage>> GetByRoleAsync(
        MessageRole role, CancellationToken ct = default)
        => await _set.Where(m => m.Role == role).ToListAsync(ct);
}
