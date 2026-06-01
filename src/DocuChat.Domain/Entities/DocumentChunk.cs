namespace DocuChat.Domain.Entities;

public class DocumentChunk : BaseEntity
{
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public string? ImagePath { get; set; }
    public string? Header { get; set; }

    public string? Summary { get; set; }
    public int? PageNumber { get; set; }

    public string? CleanContent { get; set; }          // markdown stripped — embed + tsvector için
    public string? StructuredTableJson { get; set; }   // sadece Table chunk'larında (JSONB)
    public int? TokenCount { get; set; }
    public Guid? PrevChunkId { get; set; }
    public Guid? NextChunkId { get; set; }
    public string? ContentHash { get; set; }           // değişince reprocess cache invalidation tetiklenir

    public Document? Document { get; set; }
}
