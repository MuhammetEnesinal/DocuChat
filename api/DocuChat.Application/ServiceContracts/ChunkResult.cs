namespace DocuChat.Application.ServiceContracts;

public record ChunkResult(
    string FileName,
    string Content,
    string? ImagePath = null,
    string? Header = null,
    int? PageNumber = null,
    // QuestionCache.SourceDocumentIds için: cache'e yazılan cevabın hangi belgelerden üretildiği.
    // Per-document cache invalidation (DeleteByDocumentIdAsync) selective çalışsın diye.
    // Cache hit yolundaki ChunkResult'larda null olabilir (yeniden lookup yapılmıyor).
    Guid? DocumentId = null);
