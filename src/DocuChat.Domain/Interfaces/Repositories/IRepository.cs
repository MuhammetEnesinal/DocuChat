using DocuChat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DocuChat.Domain.Interfaces.Repositories
{
    public interface IRepository
    {
        public interface IRepository<T> where T : BaseEntity
        {
            Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
            Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
            Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> pred, CancellationToken ct = default);
            Task<bool> ExistsAsync(Expression<Func<T, bool>> pred, CancellationToken ct = default);
            Task AddAsync(T entity, CancellationToken ct = default);
            void Update(T entity);
            void Delete(T entity);
        }

    }
}
