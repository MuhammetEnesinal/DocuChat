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

    public Document? Document { get; set; }
    public List<ChunkImage> ChunkLinks { get; set; } = new();
}
