using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
namespace DocuChat.Domain.Entities.Caching;

public class QuestionCache : BaseEntity
{
    public string QuestionText { get; set; } = string.Empty;
    public float[] QuestionVector { get; set; } = Array.Empty<float>();
    public string Answer { get; set; } = string.Empty;
    public string? ImagesJson { get; set; }
    public int HitCount { get; set; } = 0;
    // İlk yazımda henüz hit yok → null. İlk hit'te (IncrementHitAsync) doldurulur.
    public DateTime? LastHitAt { get; set; }

    // Bu cache entry'nin cevabını üretirken referans edilen belge ID'leri (CSV).
    // Per-document cache invalidation için kullanılır. Belge reprocess/delete olunca
    // bu sütundaki ID'yi içeren tüm cache entries silinir.
    // Null = kaynağı bilinmeyen kayıt → güvenlik için ClearAll fallback'ine tabi.
    public string? SourceDocumentIds { get; set; }

    // Departman izolasyonu: bu cache kaydının ait olduğu departman. Kullanıcı yalnız kendi
    // departmanlarına ait cache kayıtlarını okuyabilir → A departmanının cevabı B'ye sızmaz.
    // Null = global kapsam (admin tarafından üretilmiş kayıt); normal kullanıcıya servis edilmez.
    public Guid? DepartmentId { get; set; }
}
