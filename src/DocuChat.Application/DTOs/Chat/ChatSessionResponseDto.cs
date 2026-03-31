namespace DocuChat.Application.DTOs.Chat;

public record ChatSessionResponseDto(
    Guid Id,
    Guid DocumentId,
    string DocumentName,
    string Title,
    DateTime CreatedAt);