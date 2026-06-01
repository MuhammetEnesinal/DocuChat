namespace DocuChat.Application.DTOs.Chat;

public record AskRequestDto(
    string Question,
    Guid? SessionId,
    bool SkipClarification = false);
