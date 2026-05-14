using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Interfaces.Services;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<Result<AuthResponseDto>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(CancellationToken ct = default);
    Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken ct = default);
    Task<Result<UserSummaryResponseDto>> UpdateUserAsync(string userId, UpdateUserRequest req, CancellationToken ct = default);
    Task<Result<bool>> ForgotPasswordAsync(string email, CancellationToken ct = default);
    Task<Result<bool>> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct = default);
    Task<Result<UserSummaryResponseDto>> GetMeAsync(string userId, CancellationToken ct = default);
    Task<Result<bool>> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default);
}