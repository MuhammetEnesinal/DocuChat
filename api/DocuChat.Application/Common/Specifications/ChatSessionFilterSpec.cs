namespace DocuChat.Application.Common.Specifications;

public enum ChatSessionSortBy { CreatedAt, Title }

// Chat session listeleme filtre + sıralama + sayfalama parametreleri.
// Repository imzasını sade tutar; yeni filtre eklemek imzayı kırmadan yapılır.
public class ChatSessionFilterSpec
{
    public string UserId { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public ChatSessionSortBy SortBy { get; set; }
    public bool Ascending { get; set; }
    // null = filtreleme yok (hepsi); true = sadece arşivlenmişler; false = sadece arşivlenmemişler.
    public bool? Archived { get; set; }

    public ChatSessionFilterSpec(
        string UserId,
        int Page,
        int PageSize,
        DateTime? DateFrom = null,
        DateTime? DateTo = null,
        ChatSessionSortBy SortBy = ChatSessionSortBy.CreatedAt,
        bool Ascending = false,
        bool? Archived = false)
    {
        this.UserId = UserId;
        this.Page = Page;
        this.PageSize = PageSize;
        this.DateFrom = DateFrom;
        this.DateTo = DateTo;
        this.SortBy = SortBy;
        this.Ascending = Ascending;
        this.Archived = Archived;
    }

    // Controller string'inden enum'a parse — bilinmeyen değerde CreatedAt'e düşer.
    public static ChatSessionSortBy ParseSortBy(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            "title" => ChatSessionSortBy.Title,
            _ => ChatSessionSortBy.CreatedAt
        };
}
