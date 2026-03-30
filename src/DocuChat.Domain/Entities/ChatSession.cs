using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocuChat.Domain.Entities
{
    public class ChatSession : BaseEntity
    {
        // --- Scalar properties ---
        public string UserId { get; set; } = string.Empty;  
        public Guid DocumentId { get; set; }                
        public string Title { get; set; } = string.Empty;

        // --- Navigation properties ---
                             
        public Document? Document { get; set; }                  
        public List<ChatMessage> Messages { get; set; } = new();  
    }
}
