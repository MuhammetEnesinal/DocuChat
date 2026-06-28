using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IDocumentImageRepository : IRepository<DocumentImage>
{
    Task<IReadOnlyList<DocumentImage>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);
}
