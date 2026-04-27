using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Interfaces.Services;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<Result<AuthResponseDto>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(CancellationToken ct = default);
    Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken ct = default);
}