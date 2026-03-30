namespace DocuChat.Application.Abstractions;

public interface ILlmService
{
    /// Soruyu ve ilgili chunk'ları LLM'e gönderir, yanıt döner.
    /// chunks = pgvector'dan gelen en alakalı metin parçaları (context).
    
    Task<string> AskAsync(
        string question,
        IEnumerable<string> contextChunks,
        CancellationToken ct = default);
}