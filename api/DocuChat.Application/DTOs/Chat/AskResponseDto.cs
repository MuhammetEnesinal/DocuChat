namespace DocuChat.Application.DTOs.Chat;

public record AskResponseDto(
    Guid SessionId,
    string Answer,
    IEnumerable<ChunkResponseDto> SourceChunks,
    List<string>? Images = null,
    List<string>? ClarificationOptions = null,
    List<string>? FollowUpQuestions = null);
