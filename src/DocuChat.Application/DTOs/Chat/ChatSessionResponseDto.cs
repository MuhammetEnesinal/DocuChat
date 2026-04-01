namespace DocuChat.Application.DTOs.Chat;

public record ChatSessionResponseDto(
    Guid Id,
    string Title,
    DateTime CreatedAt);