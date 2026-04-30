namespace DocuChat.Application.DTOs.Document;

public record DocumentChunkDto(
    Guid Id,
    int ChunkIndex,
    string Content);