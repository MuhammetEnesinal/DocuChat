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

    // 1C: chunks içeriğinin SHA256 hash'i. Reprocess'te değişir → cache mismatch tetiklenir.
    public string? ContentHash { get; set; }

    // 4A: LLM ile üretilmiş 1-2 cümlelik konu özeti (DetectRelevantDocs için).
    public string? Summary { get; set; }

    public List<DocumentChunk> Chunks { get; set; } = new();
}