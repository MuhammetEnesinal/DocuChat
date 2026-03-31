using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class DocumentRepository : GenericRepository<Document>, IDocumentRepository
{
    public DocumentRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<Document>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
        => await _set.Where(d => d.UserId == userId)
                     .OrderByDescending(d => d.CreatedAt)
                     .ToListAsync(ct);

    public async Task<Document?> GetByIdAndUserIdAsync(
        Guid id, string userId, CancellationToken ct = default)
        => await _set.FirstOrDefaultAsync(
               d => d.Id == id && d.UserId == userId, ct);

    public async Task<Document?> GetWithChunksAsync(
        Guid id, CancellationToken ct = default)
        => await _set.Include(d => d.Chunks)
                     .FirstOrDefaultAsync(d => d.Id == id, ct);
}