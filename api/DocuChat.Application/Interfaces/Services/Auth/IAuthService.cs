using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Interfaces.Services.Auth;

// Kimlik doğrulama akışları: login, password reset, kendi profilini yönetme.
// Admin tarafından yapılan user CRUD operasyonları için IUserManagementService kullanılır.
public interface IAuthService
{
    Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
    Task<Result<bool>> ForgotPasswordAsync(string email, CancellationToken ct = default);
    Task<Result<bool>> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct = default);
    Task<Result<UserSummaryResponseDto>> GetMeAsync(string userId, CancellationToken ct = default);
    Task<Result<bool>> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default);
}
