namespace DocuChat.Application.DTOs.Chat;

public class ChatSessionResponseDto
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? PinnedAt { get; set; }
    // Son aktivite (son mesaj) zamanı — sidebar sıralaması bunu kullanır. Null ise CreatedAt.
    public DateTime? UpdatedAt { get; set; }

    public ChatSessionResponseDto(
        Guid Id, string Title, DateTime CreatedAt, bool IsArchived = false,
        DateTime? ArchivedAt = null, bool IsPinned = false, DateTime? PinnedAt = null,
        DateTime? UpdatedAt = null)
    {
        this.Id = Id;
        this.Title = Title;
        this.CreatedAt = CreatedAt;
        this.IsArchived = IsArchived;
        this.ArchivedAt = ArchivedAt;
        this.IsPinned = IsPinned;
        this.PinnedAt = PinnedAt;
        this.UpdatedAt = UpdatedAt;
    }
}
