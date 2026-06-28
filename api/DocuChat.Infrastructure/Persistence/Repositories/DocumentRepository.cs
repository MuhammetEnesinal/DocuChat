using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class DocumentRepository : GenericRepository<Document>, IDocumentRepository
{
    public DocumentRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<(Guid Id, string FileName)>> GetDocumentNamesAsync(
        CancellationToken ct = default)
    {
        var rows = await _set
            .Select(d => new { d.Id, d.FileName })
            .ToListAsync(ct);
        return rows.Select(x => (x.Id, x.FileName)).ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, string FileName, string? Summary)>> GetDocumentNamesAndSummariesAsync(
        CancellationToken ct = default)
    {
        var rows = await _set
            .Select(d => new { d.Id, d.FileName, d.Summary })
            .ToListAsync(ct);
        return rows.Select(x => (x.Id, x.FileName, x.Summary)).ToList();
    }

    public async Task<PaginatedResult<Document>> GetPagedAsync(
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        // Base query — SQL seviyesinde filtreleme + sıralama + pagination
        var query = _set.AsQueryable();
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
        string? search, CancellationToken ct = default)
    {
        var query = _set.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => EF.Functions.ILike(d.FileName, $"%{search}%"));

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> ExistsByUserAndNameAsync(
        string userId, string fileName, CancellationToken ct = default)
    {
        // PostgreSQL ILIKE — case-insensitive eşleşme (örn. "Test.pdf" == "test.pdf")
        return await _set.AnyAsync(
            d => d.UserId == userId && EF.Functions.ILike(d.FileName, fileName),
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
