namespace DocuChat.Application.DTOs.Chat;

public record AskResponseDto(
    Guid SessionId,
    string Answer,
    IEnumerable<string> SourceChunks);