namespace DocuChat.Application.DTOs.Chat;

public class ChatMessageResponseDto
{
    public Guid Id { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ImagesJson { get; set; }

    public ChatMessageResponseDto(
        Guid Id, string Role, string Content, DateTime CreatedAt, string? ImagesJson = null)
    {
        this.Id = Id;
        this.Role = Role;
        this.Content = Content;
        this.CreatedAt = CreatedAt;
        this.ImagesJson = ImagesJson;
    }
}
