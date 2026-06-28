namespace DocuChat.Domain.Enums;

public enum DocumentStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    // Reprocess başarısız olduğunda kullanılır: eski chunks aktif, RAG çalışıyor; ama belge
    // güncel değil. UI: "yeniden işleme başarısız, eski içerik aktif" badge.
    Stale = 4
}
