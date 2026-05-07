namespace DocuChat.Domain.Entities;

public class QuestionCache : BaseEntity
{
    public string QuestionText { get; set; } = string.Empty;
    public float[] QuestionVector { get; set; } = Array.Empty<float>();
    public string Answer { get; set; } = string.Empty;
    public string? ImagesJson { get; set; }
    public string? DocumentIds { get; set; }  // hangi belgeler için cache'lendi
    public int HitCount { get; set; } = 0;
    public DateTime LastHitAt { get; set; } = DateTime.UtcNow;
}