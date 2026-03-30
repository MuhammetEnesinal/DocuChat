namespace DocuChat.Domain.Entities;

public class DocumentChunk : BaseEntity
{
    public Guid DocumentId { get; set; }         
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    // EF Core config: .HasColumnType("vector(1536)")
    public float[] Embedding { get; set; } = Array.Empty<float>();

    // Navigation
    public Document? Document { get; set; }
}