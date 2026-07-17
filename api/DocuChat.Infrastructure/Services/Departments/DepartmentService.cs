using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Departments;
using DocuChat.Application.Interfaces.Services.Departments;
using DocuChat.Application.Interfaces.Services.Persistence;
using DocuChat.Domain.Entities.Departments;
using DocuChat.Infrastructure.Persistence.Context;

namespace DocuChat.Infrastructure.Services.Departments;

// Departman CRUD — yalnız admin. AppDbContext'i doğrudan kullanır (AuthService deseni).
public sealed class DepartmentService : IDepartmentService
{
    private readonly AppDbContext _db;
    private readonly IDbExceptionInspector _dbExceptionInspector;

    public DepartmentService(AppDbContext db, IDbExceptionInspector dbExceptionInspector)
    {
        _db = db;
        _dbExceptionInspector = dbExceptionInspector;
    }

    public async Task<Result<IReadOnlyList<DepartmentResponseDto>>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _db.Departments
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentResponseDto
            {
                Id = d.Id,
                Name = d.Name,
                Code = d.Code,
                CreatedAt = d.CreatedAt,
                UserCount = d.UserDepartments.Count,
                DocumentCount = d.Documents.Count,
            })
            .ToListAsync(ct);

        return Result<IReadOnlyList<DepartmentResponseDto>>.Success(list);
    }

    public async Task<Result<PaginatedResult<DepartmentResponseDto>>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        var query = _db.Departments.AsQueryable();

        // Arama: ad VEYA kod üzerinde case-insensitive (kullanıcı/belge aramasıyla aynı desen).
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(d =>
                EF.Functions.ILike(d.Name, pattern) || EF.Functions.ILike(d.Code, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(d => d.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DepartmentResponseDto
            {
                Id = d.Id,
                Name = d.Name,
                Code = d.Code,
                CreatedAt = d.CreatedAt,
                UserCount = d.UserDepartments.Count,
                DocumentCount = d.Documents.Count,
            })
            .ToListAsync(ct);

        return Result<PaginatedResult<DepartmentResponseDto>>.Success(
            new PaginatedResult<DepartmentResponseDto>(items, total, page, pageSize));
    }

    // Mükerrer kontrolü BİREBİR (büyük/küçük harf duyarlı) — Türkçe'de İ/I ve ı/i ayrı harflerdir,
    // "IT" ile "ıt" farklı kodlardır. DB'deki unique index de aynı semantiği uygular (PostgreSQL
    // varsayılan collation), yani uygulama ile şema tutarlı. excludeId: güncellemede kendini sayma.
    private async Task<Error?> FindDuplicateAsync(string name, string code, Guid? excludeId, CancellationToken ct)
    {
        if (await _db.Departments.AnyAsync(d => (excludeId == null || d.Id != excludeId) && d.Name == name, ct))
            return Error.Conflict("Bu departman adı zaten var.");
        if (await _db.Departments.AnyAsync(d => (excludeId == null || d.Id != excludeId) && d.Code == code, ct))
            return Error.Conflict("Bu departman kodu zaten var.");
        return null;
    }

    public async Task<Result<DepartmentResponseDto>> CreateAsync(
        CreateDepartmentRequestDto req, CancellationToken ct = default)
    {
        var name = (req.Name ?? string.Empty).Trim();
        var code = (req.Code ?? string.Empty).Trim();

        var duplicate = await FindDuplicateAsync(name, code, null, ct);
        if (duplicate is not null)
            return Result<DepartmentResponseDto>.Failure(duplicate);

        var dep = new Department { Name = name, Code = code };
        _db.Departments.Add(dep);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (_dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Race: kontrolden sonra paralel istek aynı ad/kodu aldı — unique index yakaladı.
            return Result<DepartmentResponseDto>.Failure(
                Error.Conflict("Bu departman adı veya kodu zaten var."));
        }

        return Result<DepartmentResponseDto>.Success(new DepartmentResponseDto
        {
            Id = dep.Id, Name = dep.Name, Code = dep.Code,
            CreatedAt = dep.CreatedAt, UserCount = 0, DocumentCount = 0,
        });
    }

    public async Task<Result<DepartmentResponseDto>> UpdateAsync(
        Guid id, UpdateDepartmentRequestDto req, CancellationToken ct = default)
    {
        var dep = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dep is null)
            return Result<DepartmentResponseDto>.Failure(Error.NotFound("Departman bulunamadı."));

        var name = (req.Name ?? string.Empty).Trim();
        var code = (req.Code ?? string.Empty).Trim();

        var duplicate = await FindDuplicateAsync(name, code, id, ct);
        if (duplicate is not null)
            return Result<DepartmentResponseDto>.Failure(duplicate);

        dep.Name = name;
        dep.Code = code;
        dep.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (_dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            return Result<DepartmentResponseDto>.Failure(
                Error.Conflict("Bu departman adı veya kodu zaten var."));
        }

        var userCount = await _db.UserDepartments.CountAsync(x => x.DepartmentId == id, ct);
        var docCount = await _db.Documents.CountAsync(x => x.DepartmentId == id, ct);
        return Result<DepartmentResponseDto>.Success(new DepartmentResponseDto
        {
            Id = dep.Id, Name = dep.Name, Code = dep.Code, CreatedAt = dep.CreatedAt,
            UserCount = userCount, DocumentCount = docCount,
        });
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var dep = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dep is null)
            return Result<bool>.Failure(Error.NotFound("Departman bulunamadı."));

        // İzolasyon güvenliği: bağlı belge veya kullanıcı varsa silme engellenir.
        if (await _db.Documents.AnyAsync(x => x.DepartmentId == id, ct))
            return Result<bool>.Failure(Error.Conflict(
                "Bu departmana bağlı belgeler var. Önce belgeleri silin veya başka departmana taşıyın."));

        if (await _db.UserDepartments.AnyAsync(x => x.DepartmentId == id, ct))
            return Result<bool>.Failure(Error.Conflict(
                "Bu departmana atanmış kullanıcılar var. Önce kullanıcı atamalarını kaldırın."));

        _db.Departments.Remove(dep);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> DeleteBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return Result<int>.Success(0);

        var candidates = await _db.Departments.Where(d => idList.Contains(d.Id)).ToListAsync(ct);
        if (candidates.Count == 0) return Result<int>.Success(0);

        // Bağlı belge/kullanıcısı olan departmanlar — tek sorguda tespit edilir (N+1 yok).
        var candidateIds = candidates.Select(d => d.Id).ToList();
        var blocked = new HashSet<Guid>(
            await _db.Documents.Where(x => candidateIds.Contains(x.DepartmentId))
                     .Select(x => x.DepartmentId).Distinct().ToListAsync(ct));
        foreach (var uid in await _db.UserDepartments.Where(x => candidateIds.Contains(x.DepartmentId))
                                     .Select(x => x.DepartmentId).Distinct().ToListAsync(ct))
            blocked.Add(uid);

        // Bağlısı olanlar ATLANIR (tekil silmedeki koruma), kalanlar silinir.
        var deletable = candidates.Where(d => !blocked.Contains(d.Id)).ToList();
        if (deletable.Count > 0)
        {
            _db.Departments.RemoveRange(deletable);
            await _db.SaveChangesAsync(ct);
        }

        return Result<int>.Success(deletable.Count);
    }
}
