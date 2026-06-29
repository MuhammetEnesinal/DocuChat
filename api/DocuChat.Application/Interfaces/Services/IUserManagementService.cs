using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Interfaces.Services;

/// <summary>
/// Admin tarafından yapılan user CRUD operasyonları.
/// IAuthService SADECE kimlik doğrulama akışları (login, password reset, self-service) için kullanılır;
/// kullanıcı yönetimi (admin tarafından yaratma/güncelleme/silme) bu interface üzerinden yapılır.
/// </summary>
public interface IUserManagementService
{
    Task<Result<UserSummaryResponseDto>> CreateUserAsync(RegisterRequestDto request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(CancellationToken ct = default);
    Task<Result<UserSummaryResponseDto>> UpdateUserAsync(string userId, UpdateUserRequestDto request, CancellationToken ct = default);
    Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken ct = default);

    // Çoklu kullanıcı silme — tek HTTP isteği, N user. Her biri için DeleteUserAsync semantiği
    // (admin self-delete koruması, son admin koruması, vb.) korunur.
    Task<Result<int>> DeleteUsersBatchAsync(IEnumerable<string> userIds, CancellationToken ct = default);

    // Excel'den toplu kullanıcı import.
    // - Header satırı atlanır
    // - Her satır validate edilir (FullName, Email, Password kuralları) — skip + report
    // - Email DB'de varsa skip
    // - Geçerli satırlar oluşturulur, welcome mail gönderilir (tek-tek CreateUserAsync gibi)
    // - Max 500 satır sınırı (controller'da kontrol edilir)
    Task<Result<BulkImportUsersSummaryDto>> BulkImportUsersFromExcelAsync(
        Stream excelStream, CancellationToken ct = default);

    // Boş Excel şablonu üretir (header satırı + 1 örnek satır).
    byte[] GenerateBulkImportTemplate();
}
