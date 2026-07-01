namespace DocuChat.Application.ServiceContracts;

public class ChunkResult
{
    public string FileName { get; set; }
    public string Content { get; set; }
    public string? ImagePath { get; set; }
    public string? Header { get; set; }
    public int? PageNumber { get; set; }
    // QuestionCache.SourceDocumentIds için: cache'e yazılan cevabın hangi belgelerden üretildiği.
    // Per-document cache invalidation (DeleteByDocumentIdAsync) selective çalışsın diye.
    // Cache hit yolundaki ChunkResult'larda null olabilir (yeniden lookup yapılmıyor).
    public Guid? DocumentId { get; set; }

    // Parametre isimleri property'lerle aynı (PascalCase) — named argument çağrıları (Content: ...)
    // eskisiyle aynı çalışsın diye.
    public ChunkResult(
        string FileName,
        string Content,
        string? ImagePath = null,
        string? Header = null,
        int? PageNumber = null,
        Guid? DocumentId = null)
    {
        this.FileName = FileName;
        this.Content = Content;
        this.ImagePath = ImagePath;
        this.Header = Header;
        this.PageNumber = PageNumber;
        this.DocumentId = DocumentId;
    }
}
