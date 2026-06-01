// DocuChat.Infrastructure/Services/AuthService.cs
using Mapster;
using DocuChat.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Auth;
using DocuChat.Domain.Enums;

namespace DocuChat.Infrastructure.Services.Auth;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtTokenService _jwtService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<AppUser> userManager,
        JwtTokenService jwtService,
        IEmailService emailService,
        IConfiguration cfg,
        ILogger<AuthService> logger)
    {
        _userManager  = userManager;
        _jwtService   = jwtService;
        _emailService = emailService;
        _cfg          = cfg;
        _logger       = logger;
    }

    public async Task<Result<AuthResponseDto>> RegisterAsync(
        RegisterRequestDto req, CancellationToken ct)
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

        _ = SendWelcomeEmailAsync(user, req.Password, ct);

        var dto = user.Adapt<AuthResponseDto>() with
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            Roles = roles
        };
        return Result<AuthResponseDto>.Success(dto);
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(
        LoginRequestDto req, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return Result<AuthResponseDto>.Failure(
                Error.Unauthorized("E-posta veya şifre hatalı."));

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtService.Generate(user, roles);

        var dto = user.Adapt<AuthResponseDto>() with
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            Roles = roles
        };
        return Result<AuthResponseDto>.Success(dto);
    }

    public async Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(
        CancellationToken ct)
    {
        var users = await _userManager.Users
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        var dtos = new List<UserSummaryResponseDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            dtos.Add(user.Adapt<UserSummaryResponseDto>() with { Roles = roles });
        }

        return Result<IReadOnlyList<UserSummaryResponseDto>>.Success(dtos);
    }

    public async Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Admin kullanıcı silinemez."));

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<bool>.Failure(Error.Validation(msg));
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<UserSummaryResponseDto>> UpdateUserAsync(
        string userId, UpdateUserRequestDto req, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<UserSummaryResponseDto>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.Admin))
            return Result<UserSummaryResponseDto>.Failure(Error.Forbidden("Admin kullanıcı güncellenemez."));

        // Update öncesi mevcut değerleri sakla → mail göndermek için
        var oldEmail = user.Email ?? string.Empty;
        var oldFullName = user.FullName ?? string.Empty;
        var emailChanged = !string.Equals(oldEmail, req.Email, StringComparison.OrdinalIgnoreCase);
        var passwordChanged = !string.IsNullOrWhiteSpace(req.Password);

        if (emailChanged)
        {
            var emailExists = await _userManager.FindByEmailAsync(req.Email);
            if (emailExists is not null)
                return Result<UserSummaryResponseDto>.Failure(Error.Conflict("Bu e-posta zaten kullanılıyor."));

            user.Email = req.Email;
            user.UserName = req.Email;
            var emailResult = await _userManager.UpdateAsync(user);
            if (!emailResult.Succeeded)
            {
                var msg = string.Join(", ", emailResult.Errors.Select(e => e.Description));
                return Result<UserSummaryResponseDto>.Failure(Error.Validation(msg));
            }
        }

        user.FullName = req.FullName;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var msg = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            return Result<UserSummaryResponseDto>.Failure(Error.Validation(msg));
        }

        if (passwordChanged)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var pwResult = await _userManager.ResetPasswordAsync(user, token, req.Password!);
            if (!pwResult.Succeeded)
            {
                var msg = string.Join(", ", pwResult.Errors.Select(e => e.Description));
                return Result<UserSummaryResponseDto>.Failure(Error.Validation(msg));
            }
        }

        _logger.LogInformation("Kullanıcı güncellendi. UserId: {UserId}", userId);

        if (emailChanged)
        {
            await SendEmailChangedNoticeAsync(oldEmail, req.Email, user.FullName ?? req.FullName, ct);
            await SendEmailChangedConfirmationAsync(req.Email, user.FullName ?? req.FullName, oldEmail, ct);
        }
        if (passwordChanged)
        {
            await SendPasswordChangedByAdminAsync(req.Email, user.FullName ?? req.FullName, req.Password!, ct);
        }

        var updatedRoles = await _userManager.GetRolesAsync(user);
        return Result<UserSummaryResponseDto>.Success(
            user.Adapt<UserSummaryResponseDto>() with { Roles = updatedRoles });
    }

    public async Task<Result<bool>> ForgotPasswordAsync(string email, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Result<bool>.Success(true); // E-posta var mı yok mu belli etme

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = Uri.EscapeDataString(token);
        var encodedEmail = Uri.EscapeDataString(email);

        var frontendUrl = _cfg["AllowedOrigins:0"] ?? "http://localhost:5173";
        var resetLink = $"{frontendUrl}/reset-password?email={encodedEmail}&token={encodedToken}";

        var body = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:auto">
              <h2 style="color:#3b82f6">Şifre Sıfırlama</h2>
              <p>Merhaba {user.FullName ?? user.Email},</p>
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

    private async Task SendEmailChangedNoticeAsync(
        string oldEmail, string newEmail, string fullName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(oldEmail)) return;

        var body = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:auto">
              <h2 style="color:#ef4444">⚠️ Hesap E-postanız Değiştirildi</h2>
              <p>Merhaba <strong>{fullName}</strong>,</p>
              <p>DocuChat hesabınızın e-posta adresi yönetici tarafından güncellendi.</p>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Eski E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{oldEmail}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Yeni E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{newEmail}</td>
                </tr>
              </table>
              <p>Bundan sonra giriş yapmak için <strong>yeni e-posta adresinizi</strong> kullanmalısınız.</p>
              <p style="color:#ef4444;font-size:13px;padding:10px;background:#fef2f2;border-left:3px solid #ef4444;border-radius:4px">
                Bu değişikliği siz talep etmediyseniz, lütfen acilen sistem yöneticinizle iletişime geçin.
              </p>
            </div>
            """;

        try
        {
            await _emailService.SendAsync(oldEmail, "DocuChat — E-posta Adresiniz Değiştirildi", body, ct);
            _logger.LogInformation("[EmailChange] Eski adrese güvenlik uyarısı gönderildi: {OldEmail}", oldEmail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EmailChange] Eski adrese mail gönderilemedi: {OldEmail}", oldEmail);
        }
    }

    private async Task SendEmailChangedConfirmationAsync(
        string newEmail, string fullName, string oldEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newEmail)) return;

        var body = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:auto">
              <h2 style="color:#3b82f6">E-posta Güncellendi</h2>
              <p>Merhaba <strong>{fullName}</strong>,</p>
              <p>DocuChat hesabınızın e-posta adresi yönetici tarafından bu adrese güncellendi.</p>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Önceki E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{oldEmail}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Yeni E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{newEmail}</td>
                </tr>
              </table>
              <p>Bundan sonra giriş için bu e-posta adresini kullanın.</p>
              <p style="color:#64748b;font-size:13px">Bu değişiklikten haberiniz yoksa sistem yöneticinizle iletişime geçin.</p>
            </div>
            """;

        try
        {
            await _emailService.SendAsync(newEmail, "DocuChat — E-posta Adresiniz Güncellendi", body, ct);
            _logger.LogInformation("[EmailChange] Yeni adrese onay gönderildi: {NewEmail}", newEmail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EmailChange] Yeni adrese mail gönderilemedi: {NewEmail}", newEmail);
        }
    }

    private async Task SendPasswordChangedByAdminAsync(
        string email, string fullName, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var body = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:auto">
              <h2 style="color:#3b82f6">Şifreniz Değiştirildi</h2>
              <p>Merhaba <strong>{fullName}</strong>,</p>
              <p>DocuChat hesabınızın şifresi yönetici tarafından sıfırlandı. Yeni giriş bilgileriniz:</p>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{email}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Yeni Şifre</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0"><code style="font-family:monospace;font-size:14px">{newPassword}</code></td>
                </tr>
              </table>
              <p style="color:#f59e0b;font-size:13px;padding:10px;background:#fffbeb;border-left:3px solid #f59e0b;border-radius:4px">
                Güvenliğiniz için ilk girişten sonra Profil sayfasından şifrenizi değiştirmenizi öneririz.
              </p>
              <p style="color:#64748b;font-size:13px">Bu değişiklikten haberiniz yoksa sistem yöneticinizle iletişime geçin.</p>
            </div>
            """;

        try
        {
            await _emailService.SendAsync(email, "DocuChat — Şifreniz Sıfırlandı", body, ct);
            _logger.LogInformation("[PasswordChange] Yeni şifre maili gönderildi: {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PasswordChange] Mail gönderilemedi: {Email}", email);
        }
    }

    private async Task SendWelcomeEmailAsync(AppUser user, string password, CancellationToken ct)
    {
        var body = $"""
            <div style="font-family:sans-serif;max-width:480px;margin:auto">
              <h2 style="color:#3b82f6">DocuChat'e Hoş Geldiniz!</h2>
              <p>Merhaba <strong>{user.FullName ?? user.Email}</strong>,</p>
              <p>Hesabınız yönetici tarafından oluşturuldu. Giriş bilgileriniz aşağıdadır:</p>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Ad Soyad</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{user.FullName}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{user.Email}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Şifre</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{password}</td>
                </tr>
              </table>
              <p style="color:#64748b;font-size:13px">İlk girişten sonra şifrenizi değiştirmenizi öneririz.</p>
            </div>
            """;

        try
        {
            await _emailService.SendAsync(user.Email ?? string.Empty, "DocuChat — Hesabınız Oluşturuldu", body, ct);
            _logger.LogInformation("Hoş geldin maili gönderildi: {Email}", user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hoş geldin maili gönderilemedi: {Email}", user.Email);
        }
    }

    public async Task<Result<bool>> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<bool>.Failure(Error.Validation(msg));
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<UserSummaryResponseDto>> GetMeAsync(string userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<UserSummaryResponseDto>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        var roles = await _userManager.GetRolesAsync(user);
        return Result<UserSummaryResponseDto>.Success(
            user.Adapt<UserSummaryResponseDto>() with { Roles = roles });
    }

    public async Task<Result<bool>> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct)
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
}
