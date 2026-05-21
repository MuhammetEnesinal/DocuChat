using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Interfaces.UseCases;

public interface IChatUseCase
{
    Task<Result<AskResponseDto>> AskAsync(AskRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ChatSessionResponseDto>>> GetMySessionsAsync(CancellationToken ct = default);

    Task<Result<PaginatedResult<ChatSessionResponseDto>>> GetMySessionsPagedAsync(
        int page, int pageSize, CancellationToken ct = default);

    Task<Result<PaginatedResult<ChatSessionResponseDto>>> GetMySessionsFilteredAsync(
        int page, int pageSize,
        DateTime? dateFrom, DateTime? dateTo,
        string sortBy, bool ascending,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<ChatMessageResponseDto>>> GetMessagesAsync(
        Guid sessionId, CancellationToken ct = default);

    Task<Result<PaginatedResult<ChatMessageResponseDto>>> GetMessagesPagedAsync(
        Guid sessionId, int page, int pageSize, CancellationToken ct = default);

    Task<Result<bool>> RenameSessionAsync(Guid sessionId, string title, CancellationToken ct = default);

    Task<Result<bool>> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);

    Task<Result<int>> DeleteSessionsBatchAsync(
        IEnumerable<Guid> sessionIds, CancellationToken ct = default);

    Task<Result<IReadOnlyList<string>>> GetPopularQuestionsAsync(int limit, CancellationToken ct = default);
}

