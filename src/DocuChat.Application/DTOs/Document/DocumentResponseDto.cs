namespace DocuChat.Application.DTOs.Document;

public record DocumentResponseDto(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Status,
    string FileType,
    int ChunkCount,
    string? ErrorMessage,
    DateTime CreatedAt);
