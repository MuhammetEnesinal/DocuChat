using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IDocumentRepository : IRepository<Document>
{
    // GetByUserIdAsync, GetByIdAndUserIdAsync, GetWithChunksAsync kaldırıldı
    // Artık admin tüm belgeleri görüyor, user bazlı filtreleme yok
}