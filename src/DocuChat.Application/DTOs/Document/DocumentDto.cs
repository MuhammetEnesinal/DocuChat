namespace DocuChat.Application.DTOs.Document;

public record DocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Status,        // DocumentStatus.ToString()
    string FileType,      // FileType.ToString()
    int ChunkCount,
    string? ErrorMessage,
    DateTime CreatedAt);