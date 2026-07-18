using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using FluentValidation;
using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Auth;
using DocuChat.Application.DTOs.Departments;
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
using DocuChat.Domain.Enums;
using DocuChat.Domain.Entities.Departments;
using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Services.UserManagement;

// Admin tarafından yapılan user CRUD operasyonları.
// ASP.NET Identity UserManager wrapper'ı + e-posta bildirimleri (welcome, email changed, password reset).
public sealed class UserManagementService : IUserManagementService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IValidator<RegisterRequestDto> _registerValidator;
    private readonly IDbExceptionInspector _dbExceptionInspector;
    private readonly AppDbContext _db;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        UserManager<AppUser> userManager,
        IEmailService emailService,
        IValidator<RegisterRequestDto> registerValidator,
        IDbExceptionInspector dbExceptionInspector,
        AppDbContext db,
        ILogger<UserManagementService> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _registerValidator = registerValidator;
        _dbExceptionInspector = dbExceptionInspector;
        _db = db;
        _logger = logger;
    }

    // ── Departman yardımcıları ──

    // Verilen ID'lerin tümü DB'de var mı? Varsa (Id+Ad) listesini döner, eksik varsa null.
    private async Task<List<DepartmentBriefDto>?> ResolveDepartmentsByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var found = await _db.Departments
            .Where(d => distinct.Contains(d.Id))
            .Select(d => new DepartmentBriefDto { Id = d.Id, Name = d.Name, Code = d.Code })
            .ToListAsync(ct);
        return found.Count == distinct.Count ? found : null;
    }

    // Kullanıcının departman atamalarını verilen ID kümesiyle DEĞİŞTİRİR (mevcutları siler,
    // yenileri ekler). SaveChanges çağıran metodun sorumluluğunda.
    private async Task ReplaceUserDepartmentsAsync(string userId, IEnumerable<Guid> deptIds, CancellationToken ct)
    {
        var existing = await _db.UserDepartments.Where(ud => ud.UserId == userId).ToListAsync(ct);
        _db.UserDepartments.RemoveRange(existing);
        foreach (var id in deptIds.Distinct())
            _db.UserDepartments.Add(new UserDepartment { UserId = userId, DepartmentId = id });
    }

    // Kullanıcı(lar)ın departmanlarını tek sorguda yükler → dto doldurmak için.
    private async Task<Dictionary<string, List<DepartmentBriefDto>>> LoadUserDepartmentsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        var rows = await _db.UserDepartments
            .Where(ud => userIds.Contains(ud.UserId))
            .Select(ud => new { ud.UserId, ud.DepartmentId, Name = ud.Department!.Name, ud.Department.Code })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new DepartmentBriefDto { Id = r.DepartmentId, Name = r.Name, Code = r.Code })
                      .OrderBy(d => d.Name).ToList());
    }

    // Rol/role-only atama: kullanıcının mevcut User/Manager rolünü kaldırıp yenisini atar
    // (Admin'e dokunmaz — çağıran zaten admin'i bu yola sokmuyor).
    private async Task SetUserRoleAsync(AppUser user, string role)
    {
        var current = await _userManager.GetRolesAsync(user);
        var toRemove = current.Where(r => r == Roles.User || r == Roles.Manager).ToList();
        if (toRemove.Count > 0) await _userManager.RemoveFromRolesAsync(user, toRemove);
        await _userManager.AddToRoleAsync(user, role);
    }

    public async Task<Result<UserSummaryResponseDto>> CreateUserAsync(
        RegisterRequestDto req, CancellationToken ct = default)
    {
        if (await _userManager.FindByEmailAsync(req.Email) is not null)
            return Result<UserSummaryResponseDto>.Failure(
                Error.Conflict("Bu e-posta zaten kayıtlı."));

        if (await _userManager.Users.AnyAsync(u => u.PersonnelCode == req.PersonnelCode, ct))
            return Result<UserSummaryResponseDto>.Failure(
                Error.Conflict("Bu personel kodu zaten kullanılıyor."));

        // Departmanlar geçerli mi? (Kullanıcı oluşturmadan ÖNCE doğrula — yarım kayıt kalmasın.)
        var departments = await ResolveDepartmentsByIdsAsync(req.DepartmentIds, ct);
        if (departments is null)
            return Result<UserSummaryResponseDto>.Failure(
                Error.Validation("Bir veya daha fazla seçilen departman bulunamadı."));

        var user = new AppUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName,
            PersonnelCode = req.PersonnelCode,
        };

        // Personel kodu, kullanıcının İLK ŞİFRESİ olarak kullanılır.
        IdentityResult result;
        try
        {
            result = await _userManager.CreateAsync(user, req.PersonnelCode);
        }
        catch (Exception ex) when (_dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // AnyAsync kontrolünden sonra başka istek aynı personel kodunu eklerse unique index
            // reddeder; kullanıcıya çakışma mesajı döner.
            return Result<UserSummaryResponseDto>.Failure(
                Error.Conflict("Bu personel kodu zaten kullanılıyor."));
        }
        if (!result.Succeeded)
        {
            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result<UserSummaryResponseDto>.Failure(Error.Validation(msg));
        }

        await _userManager.AddToRoleAsync(user, req.Role);
        var roles = await _userManager.GetRolesAsync(user);

        // Departman atamalarını yaz.
        await ReplaceUserDepartmentsAsync(user.Id, req.DepartmentIds, ct);
        await _db.SaveChangesAsync(ct);

        // Welcome mail fire-and-forget — SMTP hatası user oluşturmayı engellemesin
        _ = SendWelcomeEmailAsync(user, req.PersonnelCode, req.Role, departments, ct);

        _logger.LogInformation("[UserManagement] Yeni kullanıcı oluşturuldu. UserId: {UserId}, Email: {Email}, Rol: {Role}",
            user.Id, user.Email, req.Role);

        var summaryDto = user.Adapt<UserSummaryResponseDto>();
        summaryDto.Roles = roles;
        summaryDto.Departments = departments;
        return Result<UserSummaryResponseDto>.Success(summaryDto);
    }

    public async Task<Result<IReadOnlyList<UserSummaryResponseDto>>> GetAllUsersAsync(
        CancellationToken ct = default)
    {
        var users = await _userManager.Users
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        var deptMap = await LoadUserDepartmentsAsync(users.Select(u => u.Id).ToList(), ct);

        var dtos = new List<UserSummaryResponseDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var dto = user.Adapt<UserSummaryResponseDto>();
            dto.Roles = roles;
            dto.Departments = deptMap.TryGetValue(user.Id, out var d) ? d : new List<DepartmentBriefDto>();
            dtos.Add(dto);
        }

        return Result<IReadOnlyList<UserSummaryResponseDto>>.Success(dtos);
    }

    public async Task<Result<PaginatedResult<UserSummaryResponseDto>>> GetUsersPagedAsync(
        int page, int pageSize, string? search, string? role = null, CancellationToken ct = default)
    {
        var query = _userManager.Users.AsQueryable();

        // Rol filtresi — Identity rol tablolarıyla SQL seviyesinde join. GetUsersInRoleAsync
        // kullanılmadı: o tüm kullanıcıları belleğe çeker ve sayfalamayı bozardı.
        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleId = await _db.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstOrDefaultAsync(ct);
            if (roleId is null)   // bilinmeyen rol → boş sonuç (hata değil)
                return Result<PaginatedResult<UserSummaryResponseDto>>.Success(
                    new PaginatedResult<UserSummaryResponseDto>(new List<UserSummaryResponseDto>(), 0, page, pageSize));

            query = query.Where(u => _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(u =>
                (u.FullName != null && EF.Functions.ILike(u.FullName, pattern)) ||
                (u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var deptMap = await LoadUserDepartmentsAsync(users.Select(u => u.Id).ToList(), ct);

        var dtos = new List<UserSummaryResponseDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var dto = user.Adapt<UserSummaryResponseDto>();
            dto.Roles = roles;
            dto.Departments = deptMap.TryGetValue(user.Id, out var d) ? d : new List<DepartmentBriefDto>();
            dtos.Add(dto);
        }

        return Result<PaginatedResult<UserSummaryResponseDto>>.Success(
            new PaginatedResult<UserSummaryResponseDto>(dtos, total, page, pageSize));
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

        // Departmanlar geçerli mi? (Mutasyondan ÖNCE doğrula.)
        var departments = await ResolveDepartmentsByIdsAsync(req.DepartmentIds, ct);
        if (departments is null)
            return Result<UserSummaryResponseDto>.Failure(
                Error.Validation("Bir veya daha fazla seçilen departman bulunamadı."));

        var oldEmail = user.Email ?? string.Empty;
        var emailChanged = !string.Equals(oldEmail, req.Email, StringComparison.OrdinalIgnoreCase);
        var codeChanged = !string.Equals(user.PersonnelCode, req.PersonnelCode, StringComparison.Ordinal);

        // Personel kodu benzersiz olmalı — kendisi hariç başka kullanıcıda varsa reddet.
        if (codeChanged && await _userManager.Users.AnyAsync(
                u => u.PersonnelCode == req.PersonnelCode && u.Id != userId, ct))
            return Result<UserSummaryResponseDto>.Failure(
                Error.Conflict("Bu personel kodu zaten kullanılıyor."));

        if (emailChanged)
        {
            var emailExists = await _userManager.FindByEmailAsync(req.Email);
            if (emailExists is not null)
                return Result<UserSummaryResponseDto>.Failure(Error.Conflict("Bu e-posta zaten kullanılıyor."));

            user.Email = req.Email;
            user.UserName = req.Email;
        }

        user.FullName = req.FullName;
        user.PersonnelCode = req.PersonnelCode;

        IdentityResult updateResult;
        try
        {
            updateResult = await _userManager.UpdateAsync(user);
        }
        catch (Exception ex) when (_dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Race: kontrolden sonra başka istek aynı personel kodunu aldı.
            return Result<UserSummaryResponseDto>.Failure(
                Error.Conflict("Bu personel kodu zaten kullanılıyor."));
        }
        if (!updateResult.Succeeded)
        {
            var msg = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            return Result<UserSummaryResponseDto>.Failure(Error.Validation(msg));
        }

        // Rol (User/Manager) ve departman atamalarını güncelle.
        await SetUserRoleAsync(user, req.Role);
        await ReplaceUserDepartmentsAsync(userId, req.DepartmentIds, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[UserManagement] Kullanıcı güncellendi. UserId: {UserId}, Rol: {Role}", userId, req.Role);

        if (emailChanged)
        {
            await SendEmailChangedNoticeAsync(oldEmail, req.Email, user.FullName ?? req.FullName, ct);
            await SendEmailChangedConfirmationAsync(req.Email, user.FullName ?? req.FullName, oldEmail, ct);
        }

        var updatedRoles = await _userManager.GetRolesAsync(user);
        var updatedDto = user.Adapt<UserSummaryResponseDto>();
        updatedDto.Roles = updatedRoles;
        updatedDto.Departments = departments;
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

    // Per satır SSE event yield eder.
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

            // Departman KODU → Id haritası. Excel'de yalnız KOD kabul edilir (ad değil).
            // Eşleşme BİREBİR (Ordinal, büyük/küçük harf duyarlı): Türkçe'de İ/I ve ı/i AYRI
            // harflerdir; bunları katlamak "IT" ile "ıt"i aynı sayardı — oysa ikisi farklı kod olabilir.
            var deptByCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
            // Id → (Ad, Kod): hoş geldin mailinde departmanı "Ad - KOD" göstermek için. Satır
            // başına ekstra sorgu atmamak adına tek seferde belleğe alınır.
            var deptById = new Dictionary<Guid, DepartmentBriefDto>();
            foreach (var d in await _db.Departments.Select(d => new { d.Id, d.Name, d.Code }).ToListAsync(ct))
            {
                deptByCode[d.Code] = d.Id;
                deptById[d.Id] = new DepartmentBriefDto { Id = d.Id, Name = d.Name, Code = d.Code };
            }

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

                // Departman adlarını (virgülle çoklu) ID'ye çöz. Boş veya bilinmeyen ad → satır atlanır.
                var (deptIds, deptError) = ResolveDepartmentCodes(row.Value.DepartmentsRaw, deptByCode);
                BulkImportUserResultDto result;
                if (deptError is not null)
                {
                    result = new BulkImportUserResultDto(rowNum, NullIfEmpty(row.Value.Email), "skipped", deptError);
                }
                else
                {
                    var role = NormalizeRole(row.Value.RoleRaw);
                    // Departman brief'leri bellekteki haritadan çözülür (mail için "Ad - KOD").
                    var deptBriefs = deptIds
                        .Where(deptById.ContainsKey)
                        .Select(id => deptById[id])
                        .ToList();
                    result = await ProcessImportRowAsync(
                        rowNum, row.Value.FullName, row.Value.Email, row.Value.PersonnelCode, role, deptIds, deptBriefs, ct);
                }
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
    // Kolonlar: 1=Ad Soyad, 2=E-posta, 3=Personel Kodu, 4=Departman(lar) (virgülle çoklu), 5=Rol
    private static (string FullName, string Email, string PersonnelCode, string DepartmentsRaw, string RoleRaw)? ReadRow(
        IXLWorksheet worksheet, int rowNum)
    {
        var row = worksheet.Row(rowNum);
        var fullName = row.Cell(1).GetString().Trim();
        var email = row.Cell(2).GetString().Trim();
        var personnelCode = row.Cell(3).GetString().Trim();
        var departmentsRaw = row.Cell(4).GetString().Trim();
        var roleRaw = row.Cell(5).GetString().Trim();

        if (string.IsNullOrWhiteSpace(fullName)
            && string.IsNullOrWhiteSpace(email)
            && string.IsNullOrWhiteSpace(personnelCode)
            && string.IsNullOrWhiteSpace(departmentsRaw)
            && string.IsNullOrWhiteSpace(roleRaw))
        {
            return null;
        }
        return (fullName, email, personnelCode, departmentsRaw, roleRaw);
    }

    // Excel'deki rol metnini normalize eder. "Yönetici"/"Manager" → Manager, aksi halde User.
    private static string NormalizeRole(string? raw)
    {
        var r = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return r is "yönetici" or "yonetici" or "manager" ? Roles.Manager : Roles.User;
    }

    // Virgülle ayrılmış departman KOD'larını ID listesine çözer (Excel'de ad değil, kod yazılır).
    // Boşsa/bilinmiyorsa (ids, hataMesajı) döner; hata döndüyse satır atlanır. Departman zorunlu.
    private static (List<Guid> Ids, string? Error) ResolveDepartmentCodes(
        string raw, IReadOnlyDictionary<string, Guid> deptByCode)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (new List<Guid>(), "Departman kodu boş olamaz (en az bir kod gerekli).");

        var tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<Guid>();
        foreach (var token in tokens)
        {
            // Birebir eşleşme — kod tam yazılmalı (büyük/küçük harf duyarlı).
            if (deptByCode.TryGetValue(token, out var id))
            {
                if (!ids.Contains(id)) ids.Add(id);
            }
            else
            {
                return (ids, $"Departman kodu bulunamadı: '{token}' (kod birebir yazılmalı)");
            }
        }
        if (ids.Count == 0)
            return (ids, "Departman kodu boş olamaz (en az bir kod gerekli).");
        return (ids, null);
    }

    // Tek satır işler — validate, email check, create user, rol+departman ata, mail. Sonuç DTO döner.
    private async Task<BulkImportUserResultDto> ProcessImportRowAsync(
        int rowNum, string fullName, string email, string personnelCode,
        string role, List<Guid> departmentIds,
        IReadOnlyList<DepartmentBriefDto> departmentBriefs, CancellationToken ct)
    {
        var dto = new RegisterRequestDto(fullName, email, personnelCode, role, departmentIds);

        var validation = await _registerValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            var reason = string.Join(", ", validation.Errors.Select(e => e.ErrorMessage));
            return new BulkImportUserResultDto(rowNum, NullIfEmpty(email), "skipped", reason);
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
            return new BulkImportUserResultDto(rowNum, email, "skipped", "Bu e-posta zaten kayıtlı.");

        if (await _userManager.Users.AnyAsync(u => u.PersonnelCode == personnelCode, ct))
            return new BulkImportUserResultDto(rowNum, email, "skipped", "Bu personel kodu zaten kullanılıyor.");

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            PersonnelCode = personnelCode,
        };
        // Personel kodu, kullanıcının ilk şifresi olarak kullanılır.
        IdentityResult createResult;
        try
        {
            createResult = await _userManager.CreateAsync(user, personnelCode);
        }
        catch (Exception ex) when (_dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Race: aynı personel kodu paralel eklendi → unique index reddetti.
            return new BulkImportUserResultDto(rowNum, email, "skipped", "Bu personel kodu zaten kullanılıyor.");
        }
        if (!createResult.Succeeded)
        {
            var reason = string.Join(", ", createResult.Errors.Select(e => e.Description));
            return new BulkImportUserResultDto(rowNum, email, "skipped", reason);
        }

        await _userManager.AddToRoleAsync(user, role);
        await ReplaceUserDepartmentsAsync(user.Id, departmentIds, ct);
        await _db.SaveChangesAsync(ct);

        // Welcome mail fire-and-forget (SMTP hatası bulk'u durdurmaz)
        _ = SendWelcomeEmailAsync(user, personnelCode, role, departmentBriefs, CancellationToken.None);

        return new BulkImportUserResultDto(rowNum, email, "success", null);
    }

    public byte[] GenerateBulkImportTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Kullanıcılar");

        // Header satırı (1)
        ws.Cell(1, 1).Value = "Ad Soyad";
        ws.Cell(1, 2).Value = "E-posta";
        ws.Cell(1, 3).Value = "Personel Kodu";
        ws.Cell(1, 4).Value = "Departman Kod(lar)ı";
        ws.Cell(1, 5).Value = "Yetki";

        var headerRange = ws.Range(1, 1, 1, 5);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Örnek satır (2) — admin görüp formatı anlasın
        ws.Cell(2, 1).Value = "Ahmet Yılmaz";
        ws.Cell(2, 2).Value = "ahmet@firma.com";
        ws.Cell(2, 3).Value = "EMP1001";
        ws.Cell(2, 4).Value = "YAZILIM, IK";
        ws.Cell(2, 5).Value = "Personel";

        // Yardımcı satırlar — kurallar
        ws.Cell(4, 1).Value = "KURALLAR:";
        ws.Cell(4, 1).Style.Font.Bold = true;
        ws.Cell(5, 1).Value = "• Personel Kodu ilk şifredir: en az 6 karakter, harf + rakam, boşluksuz";
        ws.Cell(6, 1).Value = "• Departman Kod(lar)ı: departman ADI DEĞİL, KODU yazılır (örn. YAZILIM); çoklu için virgülle ayırın";
        ws.Cell(7, 1).Value = "• Kod BİREBİR yazılmalı (büyük/küçük harf duyarlı: 'IT' ile 'ıt' farklı kodlardır)";
        ws.Cell(9, 1).Value = "• Departman zorunludur — boş veya tanımsız kod içeren satır atlanır";
        ws.Cell(8, 1).Value = "• Yetki: 'Personel' veya 'Yönetici' (boş bırakılırsa Personel)";

        // Sütun genişliklerini ayarla
        ws.Column(1).Width = 30;
        ws.Column(2).Width = 35;
        ws.Column(3).Width = 20;
        ws.Column(4).Width = 32;
        ws.Column(5).Width = 14;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // ====== Email helpers ======

    // Rol anahtarı → kullanıcıya gösterilen Türkçe etiket (frontend roleLabel ile aynı sözlük).
    private static string RoleLabel(string? role) => role switch
    {
        Roles.Admin => "Admin",
        Roles.Manager => "Yönetici",
        Roles.User => "Personel",
        _ => role ?? "Personel",
    };

    // Departman gösterimi "Ad - KOD" (frontend departmentLabel ile aynı biçim).
    private static string DepartmentLabels(IReadOnlyList<DepartmentBriefDto>? departments)
        => departments is null || departments.Count == 0
            ? "—"
            : string.Join(", ", departments.Select(d =>
                string.IsNullOrWhiteSpace(d.Code) ? d.Name : $"{d.Name} - {d.Code}"));

    private async Task SendWelcomeEmailAsync(
        AppUser user, string password, string role,
        IReadOnlyList<DepartmentBriefDto>? departments, CancellationToken ct)
    {
        // Satır stilleri tekrar etmesin diye yerel sabitler (mail istemcileri <style> bloğunu
        // sık sık atar → inline stil zorunlu).
        const string th = "padding:11px 14px;background:#f1f5f9;font-weight:600;color:#334155;font-size:14px;border-bottom:1px solid #e2e8f0";
        const string td = "padding:11px 14px;background:#ffffff;color:#0f172a;font-size:14px;border-bottom:1px solid #e2e8f0";

        var body = $"""
            <div style="margin:0;padding:24px 12px;background:#eef2f7;font-family:-apple-system,Segoe UI,Roboto,sans-serif">
              <div style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e2e8f0">

                <div style="background:#4f46e5;padding:22px 24px">
                  <div style="color:#ffffff;font-size:19px;font-weight:700;letter-spacing:-0.2px">DocuChat'e Hoş Geldiniz</div>
                  <div style="color:#c7d2fe;font-size:13px;margin-top:4px">Hesabınız yönetici tarafından oluşturuldu</div>
                </div>

                <div style="padding:22px 24px">
                  <p style="margin:0 0 16px;color:#0f172a;font-size:15px">
                    Merhaba <strong>{Esc(user.FullName ?? user.Email)}</strong>, giriş bilgileriniz aşağıdadır:
                  </p>

                  <table style="border-collapse:collapse;width:100%;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden">
                    <tr><td style="{th}">Ad Soyad</td><td style="{td}">{Esc(user.FullName)}</td></tr>
                    <tr><td style="{th}">E-posta</td><td style="{td}">{Esc(user.Email)}</td></tr>
                    <tr><td style="{th}">Yetki</td><td style="{td}">{Esc(RoleLabel(role))}</td></tr>
                    <tr><td style="{th}">Departman</td><td style="{td}">{Esc(DepartmentLabels(departments))}</td></tr>
                    <tr>
                      <td style="{th}border-bottom:none">Personel Kodu<br><span style="font-weight:400;color:#64748b;font-size:12px">(ilk şifreniz)</span></td>
                      <td style="{td}border-bottom:none">
                        <span style="display:inline-block;padding:6px 12px;background:#eef2ff;border:1px solid #c7d2fe;border-radius:6px;font-family:Consolas,monospace;font-size:14px;color:#3730a3;font-weight:600">{Esc(password)}</span>
                      </td>
                    </tr>
                  </table>

                  <div style="margin:18px 0 0;padding:12px 14px;background:#fffbeb;border-left:3px solid #f59e0b;border-radius:0 6px 6px 0">
                    <span style="color:#92400e;font-size:13px">İlk şifreniz personel kodunuzdur. Güvenliğiniz için ilk girişten sonra değiştirmenizi öneririz.</span>
                  </div>

                  <p style="margin:16px 0 0;color:#64748b;font-size:12px">
                    Yalnızca size atanan departman(lar)ın belgelerine erişebilirsiniz.
                  </p>
                </div>
              </div>
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
              <p>Merhaba <strong>{Esc(fullName)}</strong>,</p>
              <p>DocuChat hesabınızın e-posta adresi yönetici tarafından güncellendi.</p>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Eski E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{Esc(oldEmail)}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Yeni E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{Esc(newEmail)}</td>
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
              <p>Merhaba <strong>{Esc(fullName)}</strong>,</p>
              <p>DocuChat hesabınızın e-posta adresi yönetici tarafından bu adrese güncellendi.</p>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Önceki E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{Esc(oldEmail)}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;background:#f1f5f9;font-weight:600;border-radius:4px 0 0 4px">Yeni E-posta</td>
                  <td style="padding:8px 12px;background:#f8fafc;border-radius:0 4px 4px 0">{Esc(newEmail)}</td>
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


    // Mail gövdeleri HTML (IsBodyHtml=true) — kullanıcı verisi encode edilmeden gömülmemeli.
    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
}
