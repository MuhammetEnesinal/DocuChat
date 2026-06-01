using DocuChat.Domain.Enums;

namespace DocuChat.Domain.Entities;

public class ChatMessage : BaseEntity
{
    public Guid SessionId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImagesJson { get; set; }

    public ChatSession? Session { get; set; }
}
