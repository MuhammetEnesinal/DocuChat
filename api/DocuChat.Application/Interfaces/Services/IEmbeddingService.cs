namespace DocuChat.Application.Interfaces.Services;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    // Batch embedding — tek HTTP isteğinde N metin. Belge işlemede her chunk için ayrı
    // istek yerine (600 chunk = 600 round-trip) toplu gönderim → büyük hız kazancı.
    // Sonuç input ile AYNI SIRADA döner (texts[i] → result[i]). Cache'li girdiler için
    // tekrar ağ çağrısı yapılmaz. Batch endpoint yoksa/başarısızsa tekil yola otomatik düşer.
    // Bir metnin embedding'i hiç alınamazsa o eleman null döner → caller chunk'ı atlayabilir.
    Task<IReadOnlyList<float[]?>> GetEmbeddingsAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}
