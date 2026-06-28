namespace DocuChat.Domain.Entities;

public class ChatSession : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    // Arşivleme: sidebar ana listesinden gizler, silmez. ArchivedAt tarih bilgisi (UI'da "X gün önce arşivlendi").
    public bool IsArchived { get; set; } = false;
    public DateTime? ArchivedAt { get; set; }

    // Sabitleme: liste sıralamasında EN ÜSTE alır. PinnedAt sabitlenmiş session'lar arasında sıralama için.
    public bool IsPinned { get; set; } = false;
    public DateTime? PinnedAt { get; set; }

    public List<ChatMessage> Messages { get; set; } = new();
}
