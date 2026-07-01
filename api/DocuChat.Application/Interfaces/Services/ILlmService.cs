using DocuChat.Application.Common.Results;
using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services;

public interface ILlmService
{
    // Cevap context'ini kurar: token bütçeli chunk seçimi + görsel işaretlerini ([IMG:N] →
    // global [[IMG-K]]) deterministik haritaya bağlar. Stream'den ÖNCE çağrılır; dönen ImageMap
    // cevap sonrası [[IMG-K]] işaretlerini gerçek görsele çevirmek için ChatUseCase'e taşınır.
    AnswerContext BuildAnswerContext(IEnumerable<ChunkResult> contextChunks);

    // Streaming variant — token delta'larını üretir. Provider streaming desteklemiyorsa
    // (Anthropic/Gemini) tam cevap tek delta olarak dönebilir. context: BuildAnswerContext çıktısı.
    // feedbackContext: kullanıcının geçmiş negatif feedback'lerinden üretilen ek system prompt section.
    IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerContext context,
        string question,
        IEnumerable<(string Role, string Content)>? history = null,
        string? feedbackContext = null,
        CancellationToken ct = default);

    Task<bool> IsCacheableAsync(
        string question,
        IEnumerable<(string Role, string Content)>? history = null,
        CancellationToken ct = default);

    Task<List<string>> GenerateClarificationsAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        IEnumerable<string>? availableDocuments = null,
        CancellationToken ct = default);

    // Bağlama bağımlı soruyu konuşma geçmişinden yararlanarak standalone arama metnine çevirir.
    // SADECE retrieval (embedding/BM25) için kullanılır — gösterilen ve cache'lenen soru ham kalır.
    // Hata olursa null döner, sistem ham soruya geri düşer.
    Task<string?> BuildContextualSearchQueryAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        CancellationToken ct = default);

    Task<string?> ValidateCachedAnswerAsync(
        string question,
        string cachedQuestion,
        string cachedAnswer,
        IEnumerable<(string Role, string Content)>? history = null,
        string? userFeedbackContext = null,
        CancellationToken ct = default);

    // sectionHeader: chunk'ın ait olduğu başlık zinciri (parse'da çıkarıldı), null/boş olabilir.
    // documentSummary arka plan referansı — sectionHeader ve chunkContent öncelik kazanır.
    Task<string> GenerateChunkContextAsync(
        string documentSummary,
        string? sectionHeader,
        string chunkContent,
        CancellationToken ct = default);

    /// <summary>
    /// Toplu ChunkContext üretimi. N chunk için TEK LLM call, JSON array döner.
    /// DocumentUseCase batch'leri PARALEL çağırır → 50 chunk = 5 batch × paralel = ~1 call süresi.
    /// Fail-open: hata/eksik sonuçta boş string ile doldurur.
    /// </summary>
    Task<IReadOnlyList<string>> GenerateChunkContextsBatchAsync(
        string documentSummary,
        IReadOnlyList<(string? Header, string Content)> chunks,
        CancellationToken ct = default);

    Task<string?> GenerateDocumentSummaryAsync(
        string sampleContent,
        CancellationToken ct = default);

    /// <summary>
    /// Konuşma geçmişini özetler — eski mesajları kompakt bir context'e dönüştürür.
    /// HistoryBudget'a sığmayan eski mesajlar bu yöntemle korunur (anahtar bağlam kaybı engellenir).
    /// </summary>
    Task<string?> SummarizeConversationAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının geçmiş negatif feedback'lerinden LLM system prompt'a eklenecek context section'ı üretir.
    /// Personal RAG self-correction: aynı chunks'tan tekrar yanlış cevap üretmemek için.
    /// items boşsa boş string döner.
    /// </summary>
    string BuildFeedbackContextPrompt(
        IReadOnlyList<(string Question, string Answer, string? ReasonText, IReadOnlyList<string> Categories)> items);

    Task<AnswerQualityResult> ValidateAnswerQualityAsync(
        string question,
        IEnumerable<ChunkResult> chunks,
        string answer,
        CancellationToken ct = default);

    Task<List<string>> GenerateFollowUpQuestionsAsync(
        string question,
        string answer,
        IEnumerable<ChunkResult> chunks,
        CancellationToken ct = default);

    Task<string?> GenerateImageCaptionAsync(
        byte[] imageBytes,
        string mimeType,
        string context,
        CancellationToken ct = default);
}
