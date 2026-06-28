using DocuChat.Domain.Enums;

namespace DocuChat.Domain.Entities;

public class ChatMessage : BaseEntity
{
    public Guid SessionId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImagesJson { get; set; }

    /// <summary>
    /// Sadece Assistant rolünde: bu cevabın hangi USER mesajına yanıt olduğu (explicit FK).
    /// Timestamp lookup yerine direkt link — rapid-fire/concurrent durumlar için %100 doğru.
    /// User message silinirse SetNull (assistant message kalır, FK null'a düşer).
    /// </summary>
    public Guid? ResponseToMessageId { get; set; }
    public ChatMessage? ResponseToMessage { get; set; }

    public ChatSession? Session { get; set; }
}
