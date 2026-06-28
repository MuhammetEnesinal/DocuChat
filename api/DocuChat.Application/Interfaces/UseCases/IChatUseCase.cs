using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Interfaces.UseCases;

public interface IChatUseCase
{
    // Streaming variant — SSE event'leri için object yield eder (her event JSON serialize edilir).
    // Event tipleri (payload'da "type" field'ı):
    //   start          : {sessionId}
    //   cache_hit      : {answer, images, followUps}
    //   clarification  : {options}
    //   token          : {delta}        — birden çok kez gelir
    //   complete       : {chunks, images, followUps, quality?, badge?}
    //   error          : {message}
    //   done           : {}             — son event
    IAsyncEnumerable<object> AskStreamAsync(AskRequestDto request, CancellationToken ct = default);

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

    /// <summary>
    /// Bir asistan mesajına 👍/👎 + sebep kaydeder. Personal — sadece kullanıcının kendi gelecek
    /// sorgularını etkiler. UNIQUE(UserId, MessageId) — aynı mesaja 2. feedback olamaz.
    /// </summary>
    Task<Result<FeedbackResponseDto>> AddFeedbackAsync(FeedbackRequestDto request, CancellationToken ct = default);
}

