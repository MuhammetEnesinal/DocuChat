namespace DocuChat.Application.Interfaces.Services;

/// <summary>
/// CLIP tabanlı görsel embedding — resmi ve metni AYNI vektör uzayına koyar.
/// Böylece bir sorunun (metin) hangi resimlere (görsel) yakın olduğu cosine ile bulunur.
/// Yerel sidecar servisine (rerank-service /embed-image, /embed-text) HTTP ile bağlanır.
/// </summary>
public interface IImageEmbeddingService
{
    /// <summary>Servis erişilebilir mi (config'de açık + sidecar ayakta).</summary>
    bool Enabled { get; }

    /// <summary>
    /// Resim byte'larını CLIP görsel vektörüne (512-dim) çevirir. Sonuç input ile AYNI sırada;
    /// decode edilemeyen/başarısız resim için eleman null döner.
    /// </summary>
    Task<IReadOnlyList<float[]?>> EmbedImagesAsync(
        IReadOnlyList<byte[]> images, CancellationToken ct = default);

    /// <summary>
    /// Metni CLIP uzayına (512-dim) çevirir — resim vektörleriyle aynı uzay, cosine'la karşılaştırılır.
    /// Başarısızsa null.
    /// </summary>
    Task<float[]?> EmbedTextAsync(string text, CancellationToken ct = default);
}
