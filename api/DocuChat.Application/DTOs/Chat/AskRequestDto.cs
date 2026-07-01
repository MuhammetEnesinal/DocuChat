namespace DocuChat.Application.DTOs.Chat;

public class AskRequestDto
{
    public string Question { get; set; }
    public Guid? SessionId { get; set; }
    public bool SkipClarification { get; set; }

    public AskRequestDto(string Question, Guid? SessionId, bool SkipClarification = false)
    {
        this.Question = Question;
        this.SessionId = SessionId;
        this.SkipClarification = SkipClarification;
    }
}
