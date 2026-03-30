using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Auth;
using Microsoft.AspNetCore.Identity.Data;

namespace DocuChat.Application.Abstractions;

public interface IUserService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest req, CancellationToken ct = default);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest req, CancellationToken ct = default);
}