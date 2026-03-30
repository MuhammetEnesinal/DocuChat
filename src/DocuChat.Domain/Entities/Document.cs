using DocuChat.Domain.Enums;
using Microsoft.VisualBasic.FileIO;

namespace DocuChat.Domain.Entities;

public class Document : BaseEntity
{
   
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? StoragePath { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public FileType FileType { get; set; } = FileType.Pdf;
    public string? ErrorMessage { get; set; }
    public int ChunkCount { get; set; }

    // Navigation
    public List<DocumentChunk> Chunks { get; set; } = new();
    public List<ChatSession> Sessions { get; set; } = new();
}