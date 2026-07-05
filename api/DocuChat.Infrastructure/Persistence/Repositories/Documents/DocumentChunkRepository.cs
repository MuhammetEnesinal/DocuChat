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

public class DocumentChunkRepository : GenericRepository<DocumentChunk>, IDocumentChunkRepository
{
    public DocumentChunkRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<DocumentChunk>> GetByDocumentIdAsync(
        Guid documentId, CancellationToken ct = default)
    {
        // ImageLinks + Image: belge chunk listesi için görsel path'leri DTO mapping'inde gerekli
        return await _set
            .Where(c => c.DocumentId == documentId)
            .Include(c => c.ImageLinks)
                .ThenInclude(il => il.Image)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);
    }
}
