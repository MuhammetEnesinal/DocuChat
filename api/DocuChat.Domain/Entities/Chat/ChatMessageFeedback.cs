using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
namespace DocuChat.Domain.Entities.Chat;

/// <summary>
/// Kullanıcının chat mesajına verdiği geri bildirim.
/// User-scoped: bir kullanıcının feedback'i SADECE kendi gelecek sorgularını etkiler.
/// Cascade: ChatMessage silinince feedback de silinir (MessageId FK).
/// </summary>
public class ChatMessageFeedback : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public Guid MessageId { get; set; }

    /// <summary>Soru metni — chunks silinse bile analitik için kalır.</summary>
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>Verilen cevap metni — analitik + LLM prompt için.</summary>
    public string AnswerText { get; set; } = string.Empty;

    /// <summary>+1 = like, -1 = dislike. Diğer değerler kabul edilmez (validator).</summary>
    public int Rating { get; set; }

    /// <summary>
    /// Soru metninin BGE-M3 embedding'i (1024-dim). HNSW index ile vector search yapılır.
    /// Yeni sorgu geldiğinde: kullanıcının eski dislike'larından SORU BENZERLİĞİ ile match edilir.
    /// Chunk match yerine bu kullanılıyor — alakalı kalıplar yakalanır, alakasızlar atlanır.
    /// </summary>
    public float[] QuestionVector { get; set; } = Array.Empty<float>();

    /// <summary>Sebep kategorileri (multi-select): wrong_info, missing_info, nonsense, doc_mismatch, image_issue.</summary>
    public List<string> ReasonCategories { get; set; } = new();

    /// <summary>Serbest metin (opsiyonel, max 500 char) — LLM'e "neden yanlıştı" detayı.</summary>
    public string? ReasonText { get; set; }

    public ChatMessage? Message { get; set; }
}
