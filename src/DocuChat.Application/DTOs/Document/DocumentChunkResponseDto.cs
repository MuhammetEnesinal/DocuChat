namespace DocuChat.Application.DTOs.Document;

public record DocumentChunkResponseDto(
    Guid Id,
    int ChunkIndex,
    string Content,
    string? ImagePath);