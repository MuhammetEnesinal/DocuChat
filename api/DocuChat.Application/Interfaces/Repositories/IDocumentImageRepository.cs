using DocuChat.Domain.Entities;

namespace DocuChat.Application.Interfaces.Repositories;

public interface IDocumentImageRepository : IRepository<DocumentImage>
{
    Task<IReadOnlyList<DocumentImage>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Verilen path'ler için CLIP görsel embedding'lerini döner (yalnızca embedding'i olanlar).
    /// Soru-cevap'ta resim seçimi için: kaynak chunk'lardaki resimlerin vektörleri çekilip
    /// soru vektörüyle cosine karşılaştırılır.
    /// </summary>
    Task<IReadOnlyDictionary<string, float[]>> GetVisualEmbeddingsByPathsAsync(
        IReadOnlyCollection<string> paths, CancellationToken ct = default);
}
