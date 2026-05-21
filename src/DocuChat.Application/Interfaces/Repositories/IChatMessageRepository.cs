using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Application.Interfaces.Repositories;

/// <summary>
/// ChatMessage'a özgü query'ler. Generic CRUD'a ek olarak session/role filter'ları.
/// </summary>
public interface IChatMessageRepository : IRepository<ChatMessage>
{
    /// Verilen oturuma ait mesaj sayısı (pagination için).
    Task<int> CountBySessionAsync(Guid sessionId, CancellationToken ct = default);

    /// Belirli role sahip tüm mesajlar (örn. popüler sorular için tüm User mesajları).
    Task<IReadOnlyList<ChatMessage>> GetByRoleAsync(MessageRole role, CancellationToken ct = default);
}
