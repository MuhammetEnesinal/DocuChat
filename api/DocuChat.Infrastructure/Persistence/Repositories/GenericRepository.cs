
using Microsoft.EntityFrameworkCore;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Repositories;

public class GenericRepository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly AppDbContext _db;
    protected readonly DbSet<T> _set;

    public GenericRepository(AppDbContext db)
    {
        _db = db;
        _set = db.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _set.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<T>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<T>();
        return await _set.Where(e => idList.Contains(e.Id)).ToListAsync(ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await _set.AddAsync(entity, ct);

    public void Delete(T entity) => _set.Remove(entity);
}
