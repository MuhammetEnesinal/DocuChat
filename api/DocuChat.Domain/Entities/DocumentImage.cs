namespace DocuChat.Domain.Entities;

/// <summary>
/// Belgeden çıkarılan görsel (Mistral OCR figüre, PdfPig embedded, XLSX picture).
/// Birden fazla chunk aynı görseli referans edebilir → DocumentChunks ile ChunkImage join.
/// </summary>
public class DocumentImage : BaseEntity
{
    public Guid DocumentId { get; set; }

    /// <summary>Diskteki dosya yolu (LocalFileStorage)</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Belge içindeki sayfa numarası (1-bazlı, varsa).</summary>
    public int? PageNumber { get; set; }

    /// <summary>SHA256 görsel byte'larından — duplicate tespit için.</summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// CLIP görsel embedding'i (512-dim). Resmi ve metni aynı vektör uzayına koyar;
    /// soru-görsel benzerliğiyle hangi resmin gösterileceğine deterministik karar verilir.
    /// Null = henüz embed edilmemiş (eski kayıt; yeniden işleme ile doldurulur).
    /// </summary>
    public float[]? VisualEmbedding { get; set; }

    public Document? Document { get; set; }
    public List<ChunkImage> ChunkLinks { get; set; } = new();
}
