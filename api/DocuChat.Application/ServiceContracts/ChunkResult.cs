namespace DocuChat.Application.ServiceContracts;

public class ChunkResult
{
    public string FileName { get; set; }
    public string Content { get; set; }
    public string? ImagePath { get; set; }
    public string? Header { get; set; }
    public int? PageNumber { get; set; }
    // Chunk'ın ait olduğu belge Id'si. Cache'e yazılan cevabın hangi belgelerden üretildiğini
    // izleyip belge-bazlı cache temizliğini (DeleteByDocumentIdAsync) beslemek için tutulur.
    // Cache hit yolundaki ChunkResult'larda null olabilir.
    public Guid? DocumentId { get; set; }

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
