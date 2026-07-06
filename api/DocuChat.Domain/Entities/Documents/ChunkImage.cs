using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
namespace DocuChat.Domain.Entities.Documents;

// Many-to-many: bir chunk birden fazla görsele, bir görsel birden fazla chunk'a sahip olabilir.
// PositionInChunk: chunk içindeki [IMG:N] markerlarındaki N (1-bazlı). LLM'in çıktısını
// frontend renderlerken hangi görseli koyacağını bilmesi için.
public class ChunkImage : BaseEntity
{
    public Guid ChunkId { get; set; }
    public Guid ImageId { get; set; }

    // Chunk içindeki [IMG:N] marker numarası (N).
    public int PositionInChunk { get; set; }

    public DocumentChunk? Chunk { get; set; }
    public DocumentImage? Image { get; set; }
}
