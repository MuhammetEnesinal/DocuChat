using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocuChat.Domain.Entities
{
    public class DocumentChunk : BaseEntity
    {
        // --- Scalar properties ---
        public Guid DocumentId { get; set; }              
        public string Content { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public float[] Embedding { get; set; } = Array.Empty<float>();

        // --- Navigation properties ---
        public Document? Document { get; set; }           
    }
}
