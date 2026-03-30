namespace DocuChat.Application.DTOs.Chat;

public record ChatSessionDto(
    Guid Id,
    Guid DocumentId,
    string DocumentName,
    string Title,
    DateTime CreatedAt);