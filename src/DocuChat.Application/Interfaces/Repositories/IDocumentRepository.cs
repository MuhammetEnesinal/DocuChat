using DocuChat.Application.Common;
using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

/// <summary>
/// Document'a özgü query'leri içerir (GenericRepository'nin CRUD'una ek olarak).
/// IChatSessionRepository ve IQuestionCacheRepository ile aynı pattern.
/// </summary>
public interface IDocumentRepository : IRepository<Document>
{
    /// DetectRelevantDocs için belge isimleri.
    Task<IReadOnlyList<(Guid Id, string FileName)>> GetDocumentNamesAsync(CancellationToken ct = default);

    /// 4A: DetectRelevantDocs için belge isimleri + LLM ile üretilmiş özetler.
    Task<IReadOnlyList<(Guid Id, string FileName, string? Summary)>> GetDocumentNamesAndSummariesAsync(CancellationToken ct = default);

    /// 1C: verilen belge ID'lerinin ContentHash'lerini döner.
    /// QuestionCache.DocumentContentHashes lookup'ı için kullanılır.
    Task<IReadOnlyList<(Guid Id, string? ContentHash)>> GetDocumentContentHashesAsync(IEnumerable<Guid> docIds, CancellationToken ct = default);

    /// SQL-level filtreleme + pagination (eski GetAllAsync().Skip().Take() in-memory pattern'inin yerine).
    /// search verilirse FileName ilike sorgusu DB seviyesinde uygulanır.
    Task<PaginatedResult<Document>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default);

    /// SQL-level search (pagination'sız) — küçük sonuç setleri için.
    Task<IReadOnlyList<Document>> SearchAsync(string? search, CancellationToken ct = default);

    /// Aynı kullanıcı tarafından aynı isimde belge yüklenmiş mi? Case-insensitive.
    /// Upload öncesi duplicate check için kullanılır.
    Task<bool> ExistsByUserAndNameAsync(string userId, string fileName, CancellationToken ct = default);
}
