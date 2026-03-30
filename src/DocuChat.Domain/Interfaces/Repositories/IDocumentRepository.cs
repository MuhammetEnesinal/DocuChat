using DocuChat.Domain.Entities;

namespace DocuChat.Domain.Interfaces.Repositories;

public interface IDocumentRepository : IRepository<Document>
{
    Task<IReadOnlyList<Document>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<Document?> GetByIdAndUserIdAsync(Guid documentId, string userId, CancellationToken ct = default);
    Task<Document?> GetWithChunksAsync(Guid documentId, CancellationToken ct = default);
}