using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Repositories;
using DocuChat.Infrastructure.Persistence.Repositories.Common;
using Microsoft.EntityFrameworkCore;
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

namespace DocuChat.Infrastructure.Persistence.Repositories.Documents;

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
