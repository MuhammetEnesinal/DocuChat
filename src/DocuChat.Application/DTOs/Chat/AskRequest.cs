namespace DocuChat.Application.DTOs.Chat;

public record AskRequest(
    string Question,
    Guid? SessionId,
    bool SkipClarification = false);