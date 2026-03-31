using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Abstractions;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<Result<AuthResponseDto>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(CancellationToken ct = default);
}