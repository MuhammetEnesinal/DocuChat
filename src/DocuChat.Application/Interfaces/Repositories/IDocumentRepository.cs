using DocuChat.Application.Common;
using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IDocumentRepository : IRepository<Document>
{
    Task<IReadOnlyList<(Guid Id, string FileName)>> GetDocumentNamesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<(Guid Id, string FileName, string? Summary)>> GetDocumentNamesAndSummariesAsync(CancellationToken ct = default);

    // SQL-level pagination + opsiyonel FileName ILIKE search.
    Task<PaginatedResult<Document>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default);

    Task<IReadOnlyList<Document>> SearchAsync(string? search, CancellationToken ct = default);

    // Aynı kullanıcı + aynı dosya adı kontrolü (case-insensitive).
    Task<bool> ExistsByUserAndNameAsync(string userId, string fileName, CancellationToken ct = default);
}
