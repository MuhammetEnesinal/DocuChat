using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocuChat.Domain.Entities
{
    public class Document : BaseEntity
    {
        // --- Scalar properties ---
        public string UserId { get; set; } = string.Empty;   
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string? StoragePath { get; set; }
        public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
        public string? ErrorMessage { get; set; }

        // --- Navigation properties ---
           
        public List<DocumentChunk> Chunks { get; set; } = new();   
        public List<ChatSession> Sessions { get; set; } = new();   
    }

}
