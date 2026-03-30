namespace DocuChat.Application.DTOs.Chat;

public record ChatMessageDto(
    Guid Id,
    string Role,
    string Content,
    DateTime CreatedAt);