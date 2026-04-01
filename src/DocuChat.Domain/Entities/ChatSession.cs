namespace DocuChat.Domain.Entities;

public class ChatSession : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    // Artık belge bazlı değil, genel sohbet oturumu
    // Navigation prop kaldırıldı
    public List<ChatMessage> Messages { get; set; } = new();
}