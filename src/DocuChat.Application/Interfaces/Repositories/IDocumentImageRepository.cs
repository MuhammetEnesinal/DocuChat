using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IDocumentImageRepository : IRepository<DocumentImage>
{
    Task<IReadOnlyList<DocumentImage>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Belge içinde aynı ContentHash'a sahip görsel varsa döner — duplicate önleme için.</summary>
    Task<DocumentImage?> FindByDocumentAndHashAsync(Guid documentId, string contentHash, CancellationToken ct = default);
}
