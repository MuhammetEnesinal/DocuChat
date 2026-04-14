using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Abstractions;

public interface IChatService
{
    Task<Result<AskResponseDto>> AskAsync(AskRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ChatSessionResponseDto>>> GetMySessionsAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<ChatMessageResponseDto>>> GetMessagesAsync(Guid sessionId, CancellationToken ct = default);
    Task<Result<bool>> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);
}