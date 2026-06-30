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

    public async Task<IReadOnlyDictionary<string, float[]>> GetVisualEmbeddingsByPathsAsync(
        IReadOnlyCollection<string> paths, CancellationToken ct = default)
    {
        if (paths.Count == 0) return new Dictionary<string, float[]>();

        var rows = await _set
            .Where(i => paths.Contains(i.Path) && i.VisualEmbedding != null)
            .Select(i => new { i.Path, i.VisualEmbedding })
            .ToListAsync(ct);

        // Aynı path birden çok kayıtta olabilir (farklı belgelerde teorik) → ilkini al.
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var r in rows)
            if (r.VisualEmbedding is { } v && !result.ContainsKey(r.Path))
                result[r.Path] = v;
        return result;
    }
}
