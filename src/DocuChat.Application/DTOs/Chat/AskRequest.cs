namespace DocuChat.Application.DTOs.Chat;

public record AskRequest(
    Guid DocumentId,
    string Question,
    Guid? SessionId);