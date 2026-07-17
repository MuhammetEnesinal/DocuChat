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
    // Netleştirme ("bunu mu demek istediniz?") seçenekleri için belge adı + özetleri.
    // departmentIds: null = filtre yok (admin); doluysa yalnız o departmanların belgeleri.
    // Filtre ŞART — belge ADI ve ÖZETİ (içerik!) LLM'e gidiyor, filtresiz olursa başka
    // departmanların belge içeriği netleştirme seçeneği olarak sızar.
    Task<IReadOnlyList<(Guid Id, string FileName, string? Summary)>> GetDocumentNamesAndSummariesAsync(
        IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default);

    // SQL-level pagination + opsiyonel FileName ILIKE search.
    // departmentIds: null = filtre yok (admin); doluysa yalnız o departmanların belgeleri (yönetici izolasyonu).
    Task<PaginatedResult<Document>> GetPagedAsync(int page, int pageSize, string? search, IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default);

    Task<IReadOnlyList<Document>> SearchAsync(string? search, IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default);

    // Aynı DEPARTMANDA aynı dosya adı var mı (case-insensitive). Kapsam departman: farklı
    // departmanlara aynı dosya yüklenebilir, aynı departmana iki kez yüklenemez.
    Task<bool> ExistsByDepartmentAndNameAsync(Guid departmentId, string fileName, CancellationToken ct = default);

    // Aynı DEPARTMANA aynı içeriğin (farklı isimle de olsa) ikinci kez yüklenmesini engellemek
    // için ContentHash eşleşmesini arar. ContentHash'i null olan kayıtlar sorguya dahil edilmez.
    Task<Document?> FindByDepartmentAndContentHashAsync(Guid departmentId, string contentHash, CancellationToken ct = default);

    // Belirli statüdeki tüm belge ID'lerini döner. DocumentRecoveryService startup'ta
    // Pending+Processing kalmış belgeleri yeniden zamanlamak için kullanır.
    Task<IReadOnlyList<Guid>> GetIdsByStatusAsync(IReadOnlyList<DocumentStatus> statuses, CancellationToken ct = default);
}
