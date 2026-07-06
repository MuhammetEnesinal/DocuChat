using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Interfaces.Services.UserManagement;

// Admin tarafından yapılan user CRUD operasyonları.
// IAuthService SADECE kimlik doğrulama akışları (login, password reset, self-service) için kullanılır;
// kullanıcı yönetimi (admin tarafından yaratma/güncelleme/silme) bu interface üzerinden yapılır.
public interface IUserManagementService
{
    Task<Result<UserSummaryResponseDto>> CreateUserAsync(RegisterRequestDto request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(CancellationToken ct = default);

    // SQL-level pagination + opsiyonel FullName/Email ILIKE search. Admin panelinde 20'şerli liste için.
    Task<Result<PaginatedResult<UserSummaryResponseDto>>> GetUsersPagedAsync(int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<Result<UserSummaryResponseDto>> UpdateUserAsync(string userId, UpdateUserRequestDto request, CancellationToken ct = default);
    Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken ct = default);

    // Çoklu kullanıcı silme — tek HTTP isteği, N user. Her biri için DeleteUserAsync semantiği
    // (admin self-delete koruması, son admin koruması, vb.) korunur.
    Task<Result<int>> DeleteUsersBatchAsync(IEnumerable<string> userIds, CancellationToken ct = default);

    // Excel'den toplu kullanıcı import — her satır işlendikçe event yield eder. Frontend SSE ile dinler
    // ve "X/N işlendi" progress bar günceller. Büyük dosyalar (2000+ satır) için.
    // Event tipleri:
    //   { type: "start",    total: N }                                         — toplam satır sayısı
    //   { type: "progress", row, email, status, reason, processed, total }    — her satır sonrası
    //   { type: "done",     summary: {totalRows, successCount, skippedCount, results} } — final
    //   { type: "error",    message }                                          — parse hatası vb.
    IAsyncEnumerable<object> BulkImportUsersFromExcelStreamAsync(
        Stream excelStream, CancellationToken ct = default);

    // Boş Excel şablonu üretir (header satırı + 1 örnek satır).
    byte[] GenerateBulkImportTemplate();
}
