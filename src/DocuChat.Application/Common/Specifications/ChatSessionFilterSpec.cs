namespace DocuChat.Application.Common.Specifications;

public enum ChatSessionSortBy { CreatedAt, Title }

// Chat session listeleme filtre + sıralama + sayfalama parametreleri.
// Repository imzasını sade tutar; yeni filtre eklemek imzayı kırmadan yapılır.
public sealed record ChatSessionFilterSpec(
    string UserId,
    int Page,
    int PageSize,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    ChatSessionSortBy SortBy = ChatSessionSortBy.CreatedAt,
    bool Ascending = false)
{
    // Controller string'inden enum'a parse — bilinmeyen değerde CreatedAt'e düşer.
    public static ChatSessionSortBy ParseSortBy(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "title" => ChatSessionSortBy.Title,
            _ => ChatSessionSortBy.CreatedAt
        };
}
