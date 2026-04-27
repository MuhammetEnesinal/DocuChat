using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;

namespace DocuChat.Application.DTOs.Chat;

public record AskResponseDto(
    Guid SessionId,
    string Answer,
    IEnumerable<ChunkResult> SourceChunks);