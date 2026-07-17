using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
using DocuChat.Domain.Entities.Departments;
using DocuChat.Domain.Enums;

namespace DocuChat.Domain.Entities.Documents;

public class Document : BaseEntity
{
    public string UserId { get; set; } = string.Empty;

    // Belgenin ait olduğu departman (zorunlu). Arama izolasyonunun anahtarı: kullanıcı yalnız
    // üye olduğu departmanların belgelerinde arama yapar.
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? StoragePath { get; set; }
    public string? ErrorMessage { get; set; }
    public int ChunkCount { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public FileType FileType { get; set; } = FileType.Pdf;

    // Dosya içeriğinin SHA256 hex'i (64 char). Aynı içeriğin farklı isimle ikinci kez
    // yüklenmesini engellemek için içerik-bazlı tekillik anahtarıdır.
    public string? ContentHash { get; set; }

    // LLM ile üretilmiş 1-2 cümlelik konu özeti.
    public string? Summary { get; set; }

    // İşleme sırasındaki yumuşak uyarılar (caption quota aşımı, chunk context fail, vb.).
    // Belge hâlâ Ready ama kullanıcı arayüzüne bilgi badge yansır.
    public string? ProcessingNotes { get; set; }

    public List<DocumentChunk> Chunks { get; set; } = new();
    public List<DocumentImage> Images { get; set; } = new();
}
