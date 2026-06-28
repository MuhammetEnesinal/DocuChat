using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

// ChunkImage join table — IRepository<T>'den gelen Add/Delete/GetById yeterli.
// Chunk başına resim çekme zaten VectorSearch'te single-query Include ile yapılıyor.
public interface IChunkImageRepository : IRepository<ChunkImage>
{
}
