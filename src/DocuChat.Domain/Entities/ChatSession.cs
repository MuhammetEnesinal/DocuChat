namespace DocuChat.Domain.Entities;

public class ChatSession : BaseEntity
{
  
    public string UserId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;

    // Navigation
    public Document? Document { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
}