using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services.Ai.Embedding;
using DocuChat.Application.Interfaces.Services.Ai.Llm;
using DocuChat.Application.Interfaces.Services.Ai.Reranker;
using DocuChat.Application.Interfaces.Services.Ai.Retrieval;
using DocuChat.Application.Interfaces.Services.Documents;
using DocuChat.Application.Interfaces.Services.Auth;
using DocuChat.Application.Interfaces.Services.UserManagement;
using DocuChat.Application.Interfaces.Services.Email;
using DocuChat.Application.Interfaces.Services.Storage;
using DocuChat.Application.Interfaces.Services.Persistence;
using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Auth;
using DocuChat.Application.DTOs.Departments;
using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Services.Auth;

// Kimlik doğrulama operasyonları: login, password reset, kendi profilini görüntüleme ve şifre değiştirme.
// Admin tarafından kullanıcı yönetimi (CRUD) IUserManagementService üzerinden yapılır.
public sealed class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtTokenService _jwtService;
    private readonly IEmailService _emailService;
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<AppUser> userManager,
        JwtTokenService jwtService,
        IEmailService emailService,
        AppDbContext db,
        IConfiguration cfg,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _jwtService = jwtService;
        _emailService = emailService;
        _db = db;
        _cfg = cfg;
        _logger = logger;
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto req, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return Result<AuthResponseDto>.Failure(
                Error.Unauthorized("E-posta veya şifre hatalı."));

        var roles = await _userManager.GetRolesAsync(user);
        var departments = await _db.UserDepartments
            .Where(ud => ud.UserId == user.Id)
            .Select(ud => new DepartmentBriefDto { Id = ud.DepartmentId, Name = ud.Department!.Name, Code = ud.Department.Code })
            .ToListAsync(ct);
        var token = _jwtService.Generate(user, roles, departments.Select(d => d.Id));

        var dto = user.Adapt<AuthResponseDto>();
        dto.Token = token;
        dto.ExpiresAt = DateTime.UtcNow.AddHours(24);
        dto.Roles = roles;
        dto.Departments = departments;
        return Result<AuthResponseDto>.Success(dto);
    }

    public async Task<Result<bool>> ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(email);
        // E-posta var mı yok mu belli etme (user enumeration koruması) — her zaman success dön
        if (user is null)
            return Result<bool>.Success(true);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = Uri.EscapeDataString(token);
        var encodedEmail = Uri.EscapeDataString(email);

        var frontendUrl = _cfg["AllowedOrigins:0"] ?? "http://localhost:5173";
        var resetLink = $"{frontendUrl}/reset-password?email={encodedEmail}&token={encodedToken}";

        var body = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:auto">
              <h2 style="color:#3b82f6">Şifre Sıfırlama</h2>
              <p>Merhaba {Esc(user.FullName ?? user.Email)},</p>
              <p>Şifrenizi sıfırlamak için aşağıdaki butona tıklayın. Link <strong>24 saat</strong> geçerlidir.</p>
              <a href="{resetLink}"
                 style="display:inline-block;margin:20px 0;padding:12px 28px;background:#3b82f6;color:#fff;border-radius:8px;text-decoration:none;font-weight:600">
                Şifremi Sıfırla
              </a>
              <p style="color:#64748b;font-size:13px">Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz.</p>
            </div>
            """;

        try
        {
            await _emailService.SendAsync(email, "DocuChat — Şifre Sıfırlama", body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şifre sıfırlama maili gönderilemedi: {Email}", email);
            return Result<bool>.Failure(Error.Validation("Mail gönderilemedi. Lütfen daha sonra tekrar deneyin."));
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ResetPasswordAsync(
        string email, string token, string newPassword, CancellationToken ct = default)
    {
        // User enumeration önleme: e-posta kayıtlı değilse de token geçersizmiş gibi aynı
        // generic mesaj döner (ForgotPassword da kayıtlılığı gizliyor — iki uç tutarlı olmalı).
        const string invalidLinkMsg =
            "Şifre sıfırlama bağlantısı geçersiz veya süresi dolmuş. Lütfen yeni bir bağlantı isteyin.";

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Result<bool>.Failure(Error.Validation(invalidLinkMsg));

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            // Token hatası da generic mesaja katlanır; şifre-politikası hataları (kısa şifre vb.)
            // kullanıcının düzeltebilmesi için aynen iletilir.
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
                return Result<bool>.Failure(Error.Validation(invalidLinkMsg));

            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<bool>.Failure(Error.Validation(msg));
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<UserSummaryResponseDto>> GetMeAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<UserSummaryResponseDto>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        var roles = await _userManager.GetRolesAsync(user);
        var departments = await _db.UserDepartments
            .Where(ud => ud.UserId == user.Id)
            .Select(ud => new DepartmentBriefDto { Id = ud.DepartmentId, Name = ud.Department!.Name, Code = ud.Department.Code })
            .ToListAsync(ct);
        var dto = user.Adapt<UserSummaryResponseDto>();
        dto.Roles = roles;
        dto.Departments = departments;
        return Result<UserSummaryResponseDto>.Success(dto);
    }

    public async Task<Result<bool>> ChangePasswordAsync(
        string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<bool>.Failure(Error.Validation(msg));
        }

        _logger.LogInformation("Şifre değiştirildi. UserId: {UserId}", userId);
        return Result<bool>.Success(true);
    }

    // Mail gövdeleri HTML (IsBodyHtml=true) — kullanıcı verisi encode edilmeden gömülmemeli.
    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
}
