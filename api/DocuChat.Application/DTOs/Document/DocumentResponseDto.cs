namespace DocuChat.Application.DTOs.Document;

public class DocumentResponseDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string Status { get; set; }
    public string FileType { get; set; }
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProcessingNotes { get; set; }
    public DateTime CreatedAt { get; set; }

    // Belgenin bağlı olduğu departman. Name/Code, Mapster flatten ile Document.Department.*'dan gelir.
    public Guid DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? DepartmentCode { get; set; }

    public DocumentResponseDto(
        Guid Id, string FileName, string ContentType, long FileSizeBytes, string Status,
        string FileType, int ChunkCount, string? ErrorMessage, string? ProcessingNotes, DateTime CreatedAt)
    {
        this.Id = Id;
        this.FileName = FileName;
        this.ContentType = ContentType;
        this.FileSizeBytes = FileSizeBytes;
        this.Status = Status;
        this.FileType = FileType;
        this.ChunkCount = ChunkCount;
        this.ErrorMessage = ErrorMessage;
        this.ProcessingNotes = ProcessingNotes;
        this.CreatedAt = CreatedAt;
    }
}
