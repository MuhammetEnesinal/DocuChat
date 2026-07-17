using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Repositories;
using DocuChat.Infrastructure.Persistence.Repositories.Common;
﻿using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Common.Results;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Interfaces.Repositories.Common;
using DocuChat.Application.Interfaces.Repositories.Chat;
using DocuChat.Application.Interfaces.Repositories.Documents;
using DocuChat.Application.Interfaces.Repositories.Caching;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
using DocuChat.Domain.Enums;

namespace DocuChat.Infrastructure.Persistence.Repositories.Documents;

public class DocumentRepository : GenericRepository<Document>, IDocumentRepository
{
    public DocumentRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<(Guid Id, string FileName, string? Summary)>> GetDocumentNamesAndSummariesAsync(
        IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default)
    {
        var query = _set.AsQueryable();
        if (departmentIds is not null)
            query = query.Where(d => departmentIds.Contains(d.DepartmentId));

        var rows = await query
            .Select(d => new { d.Id, d.FileName, d.Summary })
            .ToListAsync(ct);
        return rows.Select(x => (x.Id, x.FileName, x.Summary)).ToList();
    }

    public async Task<PaginatedResult<Document>> GetPagedAsync(
        int page, int pageSize, string? search, IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default)
    {
        // Base query — SQL seviyesinde filtreleme + sıralama + pagination.
        // Include(Department): DocumentResponseDto.DepartmentName flatten map'i için.
        var query = _set.Include(d => d.Department).AsQueryable();
        if (departmentIds is not null)
            query = query.Where(d => departmentIds.Contains(d.DepartmentId));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => EF.Functions.ILike(d.FileName, $"%{search}%"));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PaginatedResult<Document>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<Document>> SearchAsync(
        string? search, IReadOnlyList<Guid>? departmentIds = null, CancellationToken ct = default)
    {
        var query = _set.Include(d => d.Department).AsQueryable();
        if (departmentIds is not null)
            query = query.Where(d => departmentIds.Contains(d.DepartmentId));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => EF.Functions.ILike(d.FileName, $"%{search}%"));

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> ExistsByDepartmentAndNameAsync(
        Guid departmentId, string fileName, CancellationToken ct = default)
    {
        // PostgreSQL ILIKE — case-insensitive eşleşme (örn. "Test.pdf" == "test.pdf")
        return await _set.AnyAsync(
            d => d.DepartmentId == departmentId && EF.Functions.ILike(d.FileName, fileName),
            ct);
    }

    public async Task<Document?> FindByDepartmentAndContentHashAsync(
        Guid departmentId, string contentHash, CancellationToken ct = default)
    {
        return await _set.FirstOrDefaultAsync(
            d => d.DepartmentId == departmentId && d.ContentHash == contentHash,
            ct);
    }

    public async Task<IReadOnlyList<Guid>> GetIdsByStatusAsync(
        IReadOnlyList<DocumentStatus> statuses, CancellationToken ct = default)
    {
        if (statuses.Count == 0) return Array.Empty<Guid>();
        return await _set
            .Where(d => statuses.Contains(d.Status))
            .Select(d => d.Id)
            .ToListAsync(ct);
    }
}
