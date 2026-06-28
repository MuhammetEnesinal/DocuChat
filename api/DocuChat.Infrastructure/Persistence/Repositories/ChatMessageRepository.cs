using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class ChatMessageRepository : GenericRepository<ChatMessage>, IChatMessageRepository
{
    private readonly ILogger<ChatMessageRepository> _logger;

    public ChatMessageRepository(AppDbContext db, ILogger<ChatMessageRepository> logger)
        : base(db)
    {
        _logger = logger;
    }

    public async Task<int> CountBySessionAsync(Guid sessionId, CancellationToken ct = default)
        => await _set.CountAsync(m => m.SessionId == sessionId, ct);

    public async Task<IReadOnlyList<ChatMessage>> GetByRoleAsync(
        MessageRole role, CancellationToken ct = default)
        => await _set.Where(m => m.Role == role).ToListAsync(ct);

    public async Task<int> RemoveDeletedImagePathsAsync(
        IReadOnlyCollection<string> deletedImagePaths, CancellationToken ct = default)
    {
        if (deletedImagePaths.Count == 0) return 0;

        var deletedSet = new HashSet<string>(deletedImagePaths, StringComparer.OrdinalIgnoreCase);

        // Adayları indir: ImagesJson içinde silinmiş resim path'i geçen mesajlar.
        var candidates = await _set
            .Where(m => m.ImagesJson != null
                     && deletedImagePaths.Any(p => m.ImagesJson!.Contains(p)))
            .ToListAsync(ct);

        var affected = 0;
        foreach (var msg in candidates)
        {
            List<string>? paths;
            try
            {
                paths = JsonSerializer.Deserialize<List<string>>(msg.ImagesJson!);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[ChatMessage] ImagesJson parse atlandı — msgId={MessageId}", msg.Id);
                continue;
            }

            if (paths is null) continue;

            var filtered = paths.Where(p => !deletedSet.Contains(p)).ToList();
            if (filtered.Count == paths.Count) continue;  // değişmedi

            msg.ImagesJson = filtered.Count == 0
                ? null
                : JsonSerializer.Serialize(filtered);
            affected++;
        }
        return affected;
    }
}
