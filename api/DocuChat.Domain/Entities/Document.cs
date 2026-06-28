using DocuChat.Domain.Enums;

namespace DocuChat.Domain.Entities;

public class Document : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? StoragePath { get; set; }
    public string? ErrorMessage { get; set; }
    public int ChunkCount { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public FileType FileType { get; set; } = FileType.Pdf;

    // SHA256 hex (64 char). Aynı içerik farklı isim altında ikinci kez yüklenirse rejected.
    // FileName dedup yetmez (Test.pdf, Test-v2.pdf, Test-2024.pdf ↔ aynı içerik → 3 kopya).
    public string? ContentHash { get; set; }

    // LLM ile üretilmiş 1-2 cümlelik konu özeti.
    public string? Summary { get; set; }

    // İşleme sırasındaki yumuşak uyarılar (caption quota aşımı, chunk context fail, vb.).
    // Belge hâlâ Ready ama kullanıcı arayüzüne bilgi badge yansır.
    public string? ProcessingNotes { get; set; }

    public List<DocumentChunk> Chunks { get; set; } = new();
    public List<DocumentImage> Images { get; set; } = new();
}
