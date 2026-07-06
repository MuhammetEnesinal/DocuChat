using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
namespace DocuChat.Domain.Entities.Documents;

// Belgeden çıkarılan görsel (Mistral OCR figüre, PdfPig embedded, XLSX picture).
// Birden fazla chunk aynı görseli referans edebilir → DocumentChunks ile ChunkImage join.
public class DocumentImage : BaseEntity
{
    public Guid DocumentId { get; set; }

    // Diskteki dosya yolu (LocalFileStorage)
    public string Path { get; set; } = string.Empty;

    // Belge içindeki sayfa numarası (1-bazlı, varsa).
    public int? PageNumber { get; set; }

    // SHA256 görsel byte'larından — duplicate tespit için.
    public string? ContentHash { get; set; }

    public Document? Document { get; set; }
    public List<ChunkImage> ChunkLinks { get; set; } = new();
}
