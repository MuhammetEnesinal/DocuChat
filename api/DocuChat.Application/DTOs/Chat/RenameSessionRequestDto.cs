namespace DocuChat.Application.DTOs.Chat;

public class RenameSessionRequestDto
{
    public string Title { get; set; }

    public RenameSessionRequestDto(string Title)
    {
        this.Title = Title;
    }
}
