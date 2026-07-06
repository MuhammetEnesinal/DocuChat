namespace DocuChat.Domain.Enums;

public enum DocumentStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    // Reprocess başarısız olduğunda atanır: belgenin mevcut chunk'ları aktif kalır, RAG
    // çalışmaya devam eder ama içerik güncel değildir. UI'da uyarı badge'i gösterilir.
    Stale = 4
}
