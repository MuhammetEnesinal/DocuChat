using Microsoft.AspNetCore.Identity;
using DocuChat.Application.Abstractions;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Auth;
using DocuChat.Domain.Enums;
using DocuChat.Infrastructure.Identity;

namespace DocuChat.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtTokenService _jwtService;

    public AuthService(UserManager<AppUser> userManager, JwtTokenService jwtService)
    {
        _userManager = userManager;
        _jwtService = jwtService;
    }

    public async Task<Result<AuthResponseDto>> RegisterAsync(
        RegisterRequest req, CancellationToken ct)
    {
        if (await _userManager.FindByEmailAsync(req.Email) is not null)
            return Result<AuthResponseDto>.Failure(
                Error.Conflict("Bu e-posta zaten kayıtlı."));

        var user = new AppUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName,
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<AuthResponseDto>.Failure(Error.Validation(msg));
        }

        await _userManager.AddToRoleAsync(user, Roles.User);
        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtService.Generate(user, roles);

        return Result<AuthResponseDto>.Success(new AuthResponseDto(
            token, user.Id, user.Email!, user.FullName ?? string.Empty,
            DateTime.UtcNow.AddHours(24), roles));
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(
        LoginRequest req, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return Result<AuthResponseDto>.Failure(
                Error.Unauthorized("E-posta veya şifre hatalı."));

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtService.Generate(user, roles);

        return Result<AuthResponseDto>.Success(new AuthResponseDto(
            token, user.Id, user.Email!, user.FullName ?? string.Empty,
            DateTime.UtcNow.AddHours(24), roles));
    }

    public async Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(
        CancellationToken ct)
    {
        var users = _userManager.Users.ToList();

        var dtos = new List<UserSummaryResponseDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            dtos.Add(new UserSummaryResponseDto(
                user.Id,
                user.Email!,
                user.FullName ?? string.Empty,
                user.CreatedAt,
                roles));
        }

        return Result<IReadOnlyList<UserSummaryResponseDto>>.Success(dtos);
    }
}