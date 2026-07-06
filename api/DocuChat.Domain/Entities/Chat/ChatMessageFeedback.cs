using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
namespace DocuChat.Domain.Entities.Chat;

// Kullanıcının chat mesajına verdiği geri bildirim.
// User-scoped: bir kullanıcının feedback'i SADECE kendi gelecek sorgularını etkiler.
// Cascade: ChatMessage silinince feedback de silinir (MessageId FK).
public class ChatMessageFeedback : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public Guid MessageId { get; set; }

    // Soru metni — chunks silinse bile analitik için kalır.
    public string QuestionText { get; set; } = string.Empty;

    // Verilen cevap metni — analitik + LLM prompt için.
    public string AnswerText { get; set; } = string.Empty;

    // +1 = like, -1 = dislike. Diğer değerler kabul edilmez (validator).
    public int Rating { get; set; }

    // Soru metninin BGE-M3 embedding'i (1024-dim), HNSW index ile aranır. Yeni sorgu geldiğinde
    // kullanıcının dislike'larıyla soru benzerliğine göre eşleştirmek için kullanılır.
    public float[] QuestionVector { get; set; } = Array.Empty<float>();

    // Sebep kategorileri (multi-select): wrong_info, missing_info, nonsense, doc_mismatch, image_issue.
    public List<string> ReasonCategories { get; set; } = new();

    // Serbest metin (opsiyonel, max 500 char) — LLM'e "neden yanlıştı" detayı.
    public string? ReasonText { get; set; }

    public ChatMessage? Message { get; set; }
}
