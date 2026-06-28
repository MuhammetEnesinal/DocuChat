using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class DocumentImageRepository : GenericRepository<DocumentImage>, IDocumentImageRepository
{
    public DocumentImageRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<DocumentImage>> GetByDocumentIdAsync(
        Guid documentId, CancellationToken ct = default)
    {
        return await _set
            .Where(i => i.DocumentId == documentId)
            .OrderBy(i => i.PageNumber)
            .ToListAsync(ct);
    }
}
