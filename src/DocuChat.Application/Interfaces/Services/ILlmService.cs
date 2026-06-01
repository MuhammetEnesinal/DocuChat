using DocuChat.Application.Common;
using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services;

public interface ILlmService
{
    // Streaming variant — token delta'larını üretir. Provider streaming desteklemiyorsa
    // (Anthropic/Gemini) tam cevap tek delta olarak dönebilir.
    IAsyncEnumerable<string> AskStreamAsync(
        string question,
        IEnumerable<ChunkResult> contextChunks,
        IEnumerable<(string Role, string Content)>? history = null,
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

    Task<string> GenerateHypotheticalDocumentAsync(
        string question,
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
        CancellationToken ct = default);

    // sectionHeader: chunk'ın ait olduğu başlık zinciri (parse'da çıkarıldı), null/boş olabilir.
    // documentSummary arka plan referansı — sectionHeader ve chunkContent öncelik kazanır.
    Task<string> GenerateChunkContextAsync(
        string documentSummary,
        string? sectionHeader,
        string chunkContent,
        CancellationToken ct = default);

    Task<string?> GenerateDocumentSummaryAsync(
        string sampleContent,
        CancellationToken ct = default);

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
