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

namespace DocuChat.Application.Interfaces.Repositories.Documents;

// ChunkImage join table — IRepository<T>'den gelen Add/Delete/GetById yeterli.
// Chunk başına resim çekme zaten VectorSearch'te single-query Include ile yapılıyor.
public interface IChunkImageRepository : IRepository<ChunkImage>
{
}
