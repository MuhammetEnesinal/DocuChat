using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Auth;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Domain.Enums;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Services.UserManagement;

/// <summary>
/// Admin tarafından yapılan user CRUD operasyonları.
/// ASP.NET Identity UserManager wrapper'ı + e-posta bildirimleri (welcome, email changed, password reset).
/// IAuthService'ten ayrıldı (concern separation: auth ≠ user management).
/// </summary>
public sealed class UserManagementService : IUserManagementService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        UserManager<AppUser> userManager,
        IEmailService emailService,
        ILogger<UserManagementService> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Result<UserSummaryResponseDto>> CreateUserAsync(
        RegisterRequestDto req, CancellationToken ct = default)
    {
        if (await _userManager.FindByEmailAsync(req.Email) is not null)
            return Result<UserSummaryResponseDto>.Failure(
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
            return Result<UserSummaryResponseDto>.Failure(Error.Validation(msg));
        }

        await _userManager.AddToRoleAsync(user, Roles.User);
        var roles = await _userManager.GetRolesAsync(user);

        // Welcome mail fire-and-forget — SMTP hatası user oluşturmayı engellemesin
        _ = SendWelcomeEmailAsync(user, req.Password, ct);

        _logger.LogInformation("[UserManagement] Yeni kullanıcı oluşturuldu. UserId: {UserId}, Email: {Email}",
            user.Id, user.Email);

        return Result<UserSummaryResponseDto>.Success(
            user.Adapt<UserSummaryResponseDto>() with { Roles = roles });
    }

    public async Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(
        CancellationToken ct = default)
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

    public async Task<Result<UserSummaryResponseDto>> UpdateUserAsync(
        string userId, UpdateUserRequestDto req, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<UserSummaryResponseDto>.Failure(Error.NotFound("Kullanıcı bulunamadı."));

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.Admin))
            return Result<UserSummaryResponseDto>.Failure(Error.Forbidden("Admin kullanıcı güncellenemez."));

        var oldEmail = user.Email ?? string.Empty;
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

        _logger.LogInformation("[UserManagement] Kullanıcı güncellendi. UserId: {UserId}", userId);

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

    public async Task<Result<bool>> DeleteUserAsync(string userId, CancellationToken ct = default)
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

        _logger.LogInformation("[UserManagement] Kullanıcı silindi. UserId: {UserId}", userId);
        return Result<bool>.Success(true);
    }

    // ====== Email helpers ======

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
            _logger.LogInformation("[UserManagement] Hoş geldin maili gönderildi: {Email}", user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UserManagement] Hoş geldin maili gönderilemedi: {Email}", user.Email);
        }
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
            _logger.LogInformation("[UserManagement] Eski adrese güvenlik uyarısı gönderildi: {OldEmail}", oldEmail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UserManagement] Eski adrese mail gönderilemedi: {OldEmail}", oldEmail);
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
            _logger.LogInformation("[UserManagement] Yeni adrese onay gönderildi: {NewEmail}", newEmail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UserManagement] Yeni adrese mail gönderilemedi: {NewEmail}", newEmail);
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
            _logger.LogInformation("[UserManagement] Yeni şifre maili gönderildi: {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UserManagement] Mail gönderilemedi: {Email}", email);
        }
    }
}
