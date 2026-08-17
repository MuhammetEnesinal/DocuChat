using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.Services.Realtime;
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
    private readonly IRealtimeNotifier _notifier;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<AppUser> userManager,
        JwtTokenService jwtService,
        IEmailService emailService,
        AppDbContext db,
        IConfiguration cfg,
        IRealtimeNotifier notifier,
        IMemoryCache cache,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _jwtService = jwtService;
        _emailService = emailService;
        _db = db;
        _cfg = cfg;
        _notifier = notifier;
        _cache = cache;
        _logger = logger;
    }

    // Şifre/reset sonrası: Identity SecurityStamp'i zaten döndürdü (SERT damga) → tüm eski token'lar
    // /refresh dahil geçersiz. Cache evict ANINDA geçerli kılar; terminate sinyali açık pencereleri
    // hemen login'e atar (offline cihazlar döndüğünde 401→refresh-fail→logout ile zaten kapanır).
    private async Task InvalidateAllSessionsAsync(string userId, CancellationToken ct)
    {
        _cache.Remove(AuthCacheKeys.Stamps(userId));
        await _notifier.NotifyUserAsync(userId, RealtimeEventTypes.SessionTerminated, null, ct);
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto req, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user is null)
            return Result<AuthResponseDto>.Failure(
                Error.Unauthorized("E-posta veya şifre hatalı."));

        var passwordOk = await _userManager.CheckPasswordAsync(user, req.Password);

        // Kilit denetimi şifre doğrulamasından sonra yapılır. Kilit mesajı hesabın var olduğunu
        // ele verdiğinden yalnız şifresini doğru giren kişiye gösterilir; şifreyi bilmeyen her
        // durumda genel hata mesajı alır ve hesabın varlığını çıkaramaz. Şifresini bilen kullanıcı
        // için ise bu bilgi giriş yapamama nedenini açıklar.
        if (await _userManager.IsLockedOutAsync(user))
        {
            if (!passwordOk)
                return Result<AuthResponseDto>.Failure(
                    Error.Unauthorized("E-posta veya şifre hatalı."));

            return Result<AuthResponseDto>.Failure(Error.Unauthorized(
                "Çok fazla hatalı giriş denemesi nedeniyle hesabınız geçici olarak kilitlendi. " +
                "Lütfen 15 dakika sonra tekrar deneyin."));
        }

        if (!passwordOk)
        {
            // Sayaç kullanıcı satırında tutulur ve eşiğe ulaşıldığında Identity hesabı kilitler.
            await _userManager.AccessFailedAsync(user);
            return Result<AuthResponseDto>.Failure(
                Error.Unauthorized("E-posta veya şifre hatalı."));
        }

        // Başarılı giriş sayacı sıfırlar; aksi halde zaman içinde biriken denemeler beklenmedik
        // bir kilitlenmeye yol açar.
        if (await _userManager.GetAccessFailedCountAsync(user) > 0)
            await _userManager.ResetAccessFailedCountAsync(user);

        var dto = await BuildAuthResponseAsync(user, ct);
        return Result<AuthResponseDto>.Success(dto);
    }

    // Mevcut, kimliği doğrulanmış kullanıcı için DB'den TAZE rol+departman okuyup yeni JWT üretir.
    // Şifre kontrolü YOK — çağıran zaten geçerli bir token'la gelmiştir. Sessiz token yenileme akışı:
    // admin kullanıcının departman/rol/e-postasını değiştirince ilgili kullanıcıya "user.refresh"
    // sinyali gider, frontend bu ucu çağırıp token'ını güncel claim'lerle tazeler (re-login gerekmez).
    public async Task<Result<AuthResponseDto>> RefreshAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<AuthResponseDto>.Failure(Error.Unauthorized("Kullanıcı bulunamadı."));

        // Kilitli hesap (ör. arka arkaya hatalı giriş) taze token almamalı.
        if (await _userManager.IsLockedOutAsync(user))
            return Result<AuthResponseDto>.Failure(Error.Unauthorized("Hesap geçici olarak kilitli."));

        var dto = await BuildAuthResponseAsync(user, ct);
        return Result<AuthResponseDto>.Success(dto);
    }

    // Login ve Refresh ortak: DB'den rol+departman okur, JWT üretir, AuthResponseDto doldurur.
    private async Task<AuthResponseDto> BuildAuthResponseAsync(AppUser user, CancellationToken ct)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var departments = await _db.UserDepartments
            .Where(ud => ud.UserId == user.Id)
            .Select(ud => new DepartmentBriefDto { Id = ud.DepartmentId, Name = ud.Department!.Name, Code = ud.Department.Code })
            .ToListAsync(ct);
        var token = _jwtService.Generate(user, roles, departments.Select(d => d.Id));

        var expiryHours = _cfg.GetValue("Jwt:ExpiryHours", 24.0);
        var dto = user.Adapt<AuthResponseDto>();
        dto.Token = token;
        dto.ExpiresAt = DateTime.UtcNow.AddHours(expiryHours);
        dto.Roles = roles;
        dto.Departments = departments;
        return dto;
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

        // ResetPassword Identity SecurityStamp'i döndürdü → varsa açık diğer oturumlar da düşer.
        await InvalidateAllSessionsAsync(user.Id, ct);
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

        // ChangePasswordAsync Identity SecurityStamp'i döndürdü → tüm cihazlardan çıkış (bu cihaz dahil).
        await InvalidateAllSessionsAsync(userId, ct);
        _logger.LogInformation("Şifre değiştirildi. UserId: {UserId}", userId);
        return Result<bool>.Success(true);
    }

    // Mail gövdeleri HTML (IsBodyHtml=true) — kullanıcı verisi encode edilmeden gömülmemeli.
    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
}
