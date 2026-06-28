namespace DocuChat.Domain.Entities;

public class DocumentChunk : BaseEntity
{
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public string? Header { get; set; }

    public int? PageNumber { get; set; }

    public string? CleanContent { get; set; }          // markdown stripped — embed + tsvector için
    public Guid? PrevChunkId { get; set; }             // komşu chunk genişletme (VectorSearchService)
    public Guid? NextChunkId { get; set; }

    public Document? Document { get; set; }
    public List<ChunkImage> ImageLinks { get; set; } = new();
}
