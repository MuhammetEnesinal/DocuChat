namespace DocuChat.Application.DTOs.Chat;

public record ChunkResponseDto(
    string FileName,
    string Content,
    string? ImagePath = null,
    string? Header = null,
    int? PageNumber = null);
