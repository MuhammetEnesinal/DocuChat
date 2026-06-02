namespace DocuChat.Domain.Entities;

/// <summary>
/// Many-to-many: bir chunk birden fazla görsele, bir görsel birden fazla chunk'a sahip olabilir.
/// PositionInChunk: chunk içindeki [IMG:N] markerlarındaki N (1-bazlı). LLM'in çıktısını
/// frontend renderlerken hangi görseli koyacağını bilmesi için.
/// </summary>
public class ChunkImage : BaseEntity
{
    public Guid ChunkId { get; set; }
    public Guid ImageId { get; set; }

    /// <summary>Chunk içindeki [IMG:N] marker numarası (N).</summary>
    public int PositionInChunk { get; set; }

    public DocumentChunk? Chunk { get; set; }
    public DocumentImage? Image { get; set; }
}
