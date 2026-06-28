namespace DocuChat.Application.DTOs.Chat;

public record FeedbackRequestDto(
    Guid MessageId,
    int Rating,
    IReadOnlyList<string>? Categories = null,
    string? ReasonText = null);

public record FeedbackResponseDto(
    Guid Id,
    DateTime CreatedAt);
