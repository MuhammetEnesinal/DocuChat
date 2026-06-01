namespace DocuChat.Domain.Entities;

public class QuestionCache : BaseEntity
{
    public string QuestionText { get; set; } = string.Empty;
    public float[] QuestionVector { get; set; } = Array.Empty<float>();
    public string Answer { get; set; } = string.Empty;
    public string? ImagesJson { get; set; }
    public int HitCount { get; set; } = 0;
    // İlk yazımda henüz hit yok → null. İlk hit'te (IncrementHitAsync) doldurulur.
    public DateTime? LastHitAt { get; set; }
}
