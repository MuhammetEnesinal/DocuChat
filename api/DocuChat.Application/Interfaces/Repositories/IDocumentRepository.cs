using DocuChat.Application.Common.Results;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

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

    // ContentHash bazlı dedup — aynı kullanıcı aynı içeriği (farklı isimle de olsa) ikinci kez yükleyemez.
    // Null ContentHash'li eski kayıtlar dahil edilmez (filter contentHash != null).
    Task<Document?> FindByUserAndContentHashAsync(string userId, string contentHash, CancellationToken ct = default);

    // Belirli statüdeki tüm belge ID'lerini döner. DocumentRecoveryService startup'ta
    // Pending+Processing kalmış (önceki run'da yarıda kalmış) belgeleri tekrar zamanlamak için.
    Task<IReadOnlyList<Guid>> GetIdsByStatusAsync(IReadOnlyList<DocumentStatus> statuses, CancellationToken ct = default);
}
