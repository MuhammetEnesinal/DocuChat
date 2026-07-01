using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using FluentValidation;
using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Common.Results;
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
    private readonly IValidator<RegisterRequestDto> _registerValidator;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        UserManager<AppUser> userManager,
        IEmailService emailService,
        IValidator<RegisterRequestDto> registerValidator,
        ILogger<UserManagementService> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _registerValidator = registerValidator;
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

        var summaryDto = user.Adapt<UserSummaryResponseDto>();
        summaryDto.Roles = roles;
        return Result<UserSummaryResponseDto>.Success(summaryDto);
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
            var dto = user.Adapt<UserSummaryResponseDto>();
            dto.Roles = roles;
            dtos.Add(dto);
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
        var updatedDto = user.Adapt<UserSummaryResponseDto>();
        updatedDto.Roles = updatedRoles;
        return Result<UserSummaryResponseDto>.Success(updatedDto);
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

    public async Task<Result<int>> DeleteUsersBatchAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var idList = userIds.Distinct().ToList();
        if (idList.Count == 0) return Result<int>.Success(0);

        var deleted = 0;
        var skippedAdmins = 0;
        var notFound = 0;
        var failedDetails = new List<string>();

        foreach (var id in idList)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user is null) { notFound++; continue; }

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains(Roles.Admin)) { skippedAdmins++; continue; }

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                deleted++;
            }
            else
            {
                failedDetails.Add($"{user.Email ?? id}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        _logger.LogInformation(
            "[UserManagement][Batch] {Deleted} silindi, {Skipped} admin atlandı, {NotFound} bulunamadı, {Failed} hata",
            deleted, skippedAdmins, notFound, failedDetails.Count);

        if (failedDetails.Count > 0)
        {
            return Result<int>.Failure(Error.Validation(
                $"{deleted} kullanıcı silindi. {failedDetails.Count} hata: {string.Join(" | ", failedDetails.Take(5))}"));
        }
        return Result<int>.Success(deleted);
    }

    // ====== Bulk Import (Excel) ======

    public async Task<Result<BulkImportUsersSummaryDto>> BulkImportUsersFromExcelAsync(
        Stream excelStream, CancellationToken ct = default)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(excelStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BulkImport] Excel parse hatası");
            return Result<BulkImportUsersSummaryDto>.Failure(
                Error.Validation("Excel dosyası okunamadı. Geçerli bir .xlsx dosyası olduğundan emin olun."));
        }

        using (workbook)
        {
            return await BulkImportFromWorkbookAsync(workbook, ct);
        }
    }

    private async Task<Result<BulkImportUsersSummaryDto>> BulkImportFromWorkbookAsync(
        XLWorkbook workbook, CancellationToken ct)
    {
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
            return Result<BulkImportUsersSummaryDto>.Failure(Error.Validation("Excel dosyasında sayfa bulunamadı."));

        var firstRow = worksheet.FirstRowUsed();
        if (firstRow is null)
            return Result<BulkImportUsersSummaryDto>.Failure(Error.Validation("Excel dosyası boş."));

        var lastRow = worksheet.LastRowUsed();
        var dataStart = firstRow.RowNumber() + 1;
        var dataEnd = lastRow!.RowNumber();

        var results = new List<BulkImportUserResultDto>();
        var successCount = 0;
        var skippedCount = 0;
        var totalRows = 0;

        for (var rowNum = dataStart; rowNum <= dataEnd; rowNum++)
        {
            ct.ThrowIfCancellationRequested();
            var row = ReadRow(worksheet, rowNum);
            if (row is null) continue;  // tamamen boş satır

            totalRows++;
            var result = await ProcessImportRowAsync(rowNum, row.Value.FullName, row.Value.Email, row.Value.Password, ct);
            results.Add(result);
            if (result.Status == "success") successCount++; else skippedCount++;
        }

        _logger.LogInformation(
            "[BulkImport] Toplam {Total} satır işlendi: {Success} oluşturuldu, {Skipped} atlandı",
            totalRows, successCount, skippedCount);

        return Result<BulkImportUsersSummaryDto>.Success(
            new BulkImportUsersSummaryDto(totalRows, successCount, skippedCount, results));
    }

    // Streaming variant — per satır SSE event yield eder.
    // Pattern: start → progress × N → done. Hata olursa error event sonrası yield break.
    public async IAsyncEnumerable<object> BulkImportUsersFromExcelStreamAsync(
        Stream excelStream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        XLWorkbook? workbook = null;
        string? parseError = null;
        try { workbook = new XLWorkbook(excelStream); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BulkImport][Stream] Excel parse hatası");
            parseError = "Excel dosyası okunamadı. Geçerli bir .xlsx dosyası olduğundan emin olun.";
        }

        if (parseError is not null)
        {
            yield return new { type = "error", message = parseError };
            yield break;
        }

        using (workbook!)
        {
            var worksheet = workbook!.Worksheets.FirstOrDefault();
            if (worksheet is null)
            {
                yield return new { type = "error", message = "Excel dosyasında sayfa bulunamadı." };
                yield break;
            }

            var firstRow = worksheet.FirstRowUsed();
            if (firstRow is null)
            {
                yield return new { type = "error", message = "Excel dosyası boş." };
                yield break;
            }

            var lastRow = worksheet.LastRowUsed();
            var dataStart = firstRow.RowNumber() + 1;
            var dataEnd = lastRow!.RowNumber();
            // Tahmini toplam — gerçek toplam boş satırları çıkarınca farklı olabilir
            var totalEstimate = Math.Max(0, dataEnd - dataStart + 1);

            yield return new { type = "start", total = totalEstimate };

            var results = new List<BulkImportUserResultDto>();
            var successCount = 0;
            var skippedCount = 0;
            var processed = 0;
            var totalRows = 0;

            for (var rowNum = dataStart; rowNum <= dataEnd; rowNum++)
            {
                ct.ThrowIfCancellationRequested();
                var row = ReadRow(worksheet, rowNum);
                if (row is null) continue;  // tamamen boş satır — sessiz atla, progress'e dahil etme

                totalRows++;
                processed++;
                var result = await ProcessImportRowAsync(rowNum, row.Value.FullName, row.Value.Email, row.Value.Password, ct);
                results.Add(result);
                if (result.Status == "success") successCount++; else skippedCount++;

                yield return new
                {
                    type = "progress",
                    row = result.Row,
                    email = result.Email,
                    status = result.Status,
                    reason = result.Reason,
                    processed,
                    total = totalEstimate
                };
            }

            _logger.LogInformation(
                "[BulkImport][Stream] Toplam {Total} satır işlendi: {Success} oluşturuldu, {Skipped} atlandı",
                totalRows, successCount, skippedCount);

            yield return new
            {
                type = "done",
                summary = new BulkImportUsersSummaryDto(totalRows, successCount, skippedCount, results)
            };
        }
    }

    // Tek satır okur, tamamen boşsa null döner.
    private static (string FullName, string Email, string Password)? ReadRow(
        IXLWorksheet worksheet, int rowNum)
    {
        var row = worksheet.Row(rowNum);
        var fullName = row.Cell(1).GetString().Trim();
        var email = row.Cell(2).GetString().Trim();
        var password = row.Cell(3).GetString();

        if (string.IsNullOrWhiteSpace(fullName)
            && string.IsNullOrWhiteSpace(email)
            && string.IsNullOrWhiteSpace(password))
        {
            return null;
        }
        return (fullName, email, password);
    }

    // Tek satır işler — validate, email check, create user, mail. Sonuç DTO döner.
    // Hem sync hem streaming variant bu metodu kullanır (DRY).
    private async Task<BulkImportUserResultDto> ProcessImportRowAsync(
        int rowNum, string fullName, string email, string password, CancellationToken ct)
    {
        var dto = new RegisterRequestDto(fullName, email, password);

        var validation = await _registerValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            var reason = string.Join(", ", validation.Errors.Select(e => e.ErrorMessage));
            return new BulkImportUserResultDto(rowNum, NullIfEmpty(email), "skipped", reason);
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
            return new BulkImportUserResultDto(rowNum, email, "skipped", "Bu e-posta zaten kayıtlı.");

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
        };
        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var reason = string.Join(", ", createResult.Errors.Select(e => e.Description));
            return new BulkImportUserResultDto(rowNum, email, "skipped", reason);
        }

        await _userManager.AddToRoleAsync(user, Roles.User);

        // Welcome mail fire-and-forget (SMTP hatası bulk'u durdurmaz)
        _ = SendWelcomeEmailAsync(user, password, CancellationToken.None);

        return new BulkImportUserResultDto(rowNum, email, "success", null);
    }

    public byte[] GenerateBulkImportTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Kullanıcılar");

        // Header satırı (1)
        ws.Cell(1, 1).Value = "Ad Soyad";
        ws.Cell(1, 2).Value = "E-posta";
        ws.Cell(1, 3).Value = "Şifre";

        var headerRange = ws.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Örnek satır (2) — admin görüp formatı anlasın
        ws.Cell(2, 1).Value = "Ahmet Yılmaz";
        ws.Cell(2, 2).Value = "ahmet@firma.com";
        ws.Cell(2, 3).Value = "Gecici123!";

        // Yardımcı satır (3) — kurallar
        ws.Cell(4, 1).Value = "ŞIFRE KURALLARI:";
        ws.Cell(4, 1).Style.Font.Bold = true;
        ws.Cell(5, 1).Value = "• En az 8 karakter";
        ws.Cell(6, 1).Value = "• En az 1 büyük harf";
        ws.Cell(7, 1).Value = "• En az 1 küçük harf";
        ws.Cell(8, 1).Value = "• En az 1 rakam";
        ws.Cell(9, 1).Value = "• En az 1 özel karakter (!, @, #, vb.)";

        // Sütun genişliklerini ayarla
        ws.Column(1).Width = 30;
        ws.Column(2).Width = 35;
        ws.Column(3).Width = 20;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

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
