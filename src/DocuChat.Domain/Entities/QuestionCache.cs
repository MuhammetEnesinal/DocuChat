namespace DocuChat.Domain.Entities;

public class QuestionCache : BaseEntity
{
    public string QuestionText { get; set; } = string.Empty;
    public float[] QuestionVector { get; set; } = Array.Empty<float>();
    public string Answer { get; set; } = string.Empty;
    public string? ImagesJson { get; set; }
    public string? DocumentIds { get; set; }  // hangi belgeler için cache'lendi
    // 1C: ilgili belgelerin ContentHash'leri ",hash1,hash2," formatında.
    // Lookup'ta belgenin güncel hash'iyle compare edilir → reprocess sonrası stale cache yakalanır.
    public string? DocumentContentHashes { get; set; }
    public int HitCount { get; set; } = 0;
    // İlk yazımda henüz hit yok → null. İlk hit'te (IncrementHitAsync) doldurulur.
    // BaseEntity.CreatedAt giriş zamanını verir; TTL temizliği bu ikisini birlikte kullanır.
    public DateTime? LastHitAt { get; set; }
}