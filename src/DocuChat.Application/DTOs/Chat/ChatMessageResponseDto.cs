namespace DocuChat.Application.DTOs.Chat;

public record ChatMessageResponseDto(
    Guid Id,
    string Role,
    string Content,
    DateTime CreatedAt,
    string? ImagesJson = null);