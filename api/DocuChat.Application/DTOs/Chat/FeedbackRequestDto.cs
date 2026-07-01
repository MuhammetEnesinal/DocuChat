namespace DocuChat.Application.DTOs.Chat;

public class FeedbackRequestDto
{
    public Guid MessageId { get; set; }
    public int Rating { get; set; }
    public IReadOnlyList<string>? Categories { get; set; }
    public string? ReasonText { get; set; }

    public FeedbackRequestDto(
        Guid MessageId, int Rating, IReadOnlyList<string>? Categories = null, string? ReasonText = null)
    {
        this.MessageId = MessageId;
        this.Rating = Rating;
        this.Categories = Categories;
        this.ReasonText = ReasonText;
    }
}

public class FeedbackResponseDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }

    public FeedbackResponseDto(Guid Id, DateTime CreatedAt)
    {
        this.Id = Id;
        this.CreatedAt = CreatedAt;
    }
}
