namespace DocuChat.Application.DTOs.Chat;

public record ChatSessionResponseDto(
    Guid Id,
    string Title,
    DateTime CreatedAt,
    bool IsArchived = false,
    DateTime? ArchivedAt = null,
    bool IsPinned = false,
    DateTime? PinnedAt = null);
