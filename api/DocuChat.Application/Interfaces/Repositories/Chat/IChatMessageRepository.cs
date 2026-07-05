using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Interfaces.Repositories.Common;
using DocuChat.Application.Interfaces.Repositories.Chat;
using DocuChat.Application.Interfaces.Repositories.Documents;
using DocuChat.Application.Interfaces.Repositories.Caching;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
using DocuChat.Domain.Enums;

namespace DocuChat.Application.Interfaces.Repositories.Chat;

public interface IChatMessageRepository : IRepository<ChatMessage>
{
    /// Verilen oturuma ait mesaj sayısı (pagination için).
    Task<int> CountBySessionAsync(Guid sessionId, CancellationToken ct = default);

    /// Belirli role sahip tüm mesajlar (örn. popüler sorular için tüm User mesajları).
    Task<IReadOnlyList<ChatMessage>> GetByRoleAsync(MessageRole role, CancellationToken ct = default);

    Task<int> RemoveDeletedImagePathsAsync(
        IReadOnlyCollection<string> deletedImagePaths, CancellationToken ct = default);
}
