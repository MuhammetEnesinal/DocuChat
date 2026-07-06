using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Interfaces.Repositories.Common;
using DocuChat.Application.Interfaces.Repositories.Chat;
using DocuChat.Application.Interfaces.Repositories.Documents;
using DocuChat.Application.Interfaces.Repositories.Caching;
﻿using DocuChat.Application.Common.Results;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
using DocuChat.Domain.Enums;

namespace DocuChat.Application.Interfaces.Repositories.Documents;

public interface IDocumentRepository : IRepository<Document>
{
    Task<IReadOnlyList<(Guid Id, string FileName, string? Summary)>> GetDocumentNamesAndSummariesAsync(CancellationToken ct = default);

    // SQL-level pagination + opsiyonel FileName ILIKE search.
    Task<PaginatedResult<Document>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default);

    Task<IReadOnlyList<Document>> SearchAsync(string? search, CancellationToken ct = default);

    // Aynı kullanıcı + aynı dosya adı kontrolü (case-insensitive).
    Task<bool> ExistsByUserAndNameAsync(string userId, string fileName, CancellationToken ct = default);

    // Aynı kullanıcının aynı içeriği (farklı isimle de olsa) ikinci kez yüklemesini engellemek
    // için ContentHash eşleşmesini arar. ContentHash'i null olan kayıtlar sorguya dahil edilmez.
    Task<Document?> FindByUserAndContentHashAsync(string userId, string contentHash, CancellationToken ct = default);

    // Belirli statüdeki tüm belge ID'lerini döner. DocumentRecoveryService startup'ta
    // Pending+Processing kalmış belgeleri yeniden zamanlamak için kullanır.
    Task<IReadOnlyList<Guid>> GetIdsByStatusAsync(IReadOnlyList<DocumentStatus> statuses, CancellationToken ct = default);
}
