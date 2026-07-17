using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Departments;

namespace DocuChat.Application.Interfaces.Services.Departments;

// Departman yönetimi — yalnız admin. Departmanlar belge/kullanıcı izolasyonunun temel birimidir.
public interface IDepartmentService
{
    // Tam liste — seçiciler (kullanıcı modalı, belge yükleme) tüm departmanlara ihtiyaç duyar.
    Task<Result<IReadOnlyList<DepartmentResponseDto>>> GetAllAsync(CancellationToken ct = default);

    // SQL-level pagination + opsiyonel ad/kod ILIKE arama — yönetim listesi için (kullanıcı/belge deseni).
    Task<Result<PaginatedResult<DepartmentResponseDto>>> GetPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<Result<DepartmentResponseDto>> CreateAsync(CreateDepartmentRequestDto req, CancellationToken ct = default);
    Task<Result<DepartmentResponseDto>> UpdateAsync(Guid id, UpdateDepartmentRequestDto req, CancellationToken ct = default);

    // Bağlı belge veya kullanıcı varsa silinemez (referential integrity + izolasyon güvenliği).
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);

    // Çoklu silme — tek istek, N departman. Tekil silmedeki koruma korunur: bağlı belge/kullanıcı
    // olan departmanlar ATLANIR (hata vermez). Silinen sayısını döner.
    Task<Result<int>> DeleteBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
