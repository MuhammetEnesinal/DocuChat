namespace DocuChat.Application.DTOs.Chat;

public record AskRequestDto(
    Guid DocumentId,
    string Question,
    Guid? SessionId);