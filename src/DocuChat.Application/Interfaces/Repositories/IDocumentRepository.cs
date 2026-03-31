using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IDocumentRepository : IRepository<Document>
{
    Task<IReadOnlyList<Document>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<Document?> GetByIdAndUserIdAsync(Guid id, string userId, CancellationToken ct = default);
    Task<Document?> GetWithChunksAsync(Guid id, CancellationToken ct = default);
}