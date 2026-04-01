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

    
    public List<DocumentChunk> Chunks { get; set; } = new();
    public List<ChatSession> Sessions { get; set; } = new();
}