using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocuChat.Domain.Entities
{
    public class ChatMessage : BaseEntity
    {
        // --- Scalar properties ---
        public Guid SessionId { get; set; }                
        public MessageRole Role { get; set; }
        public string Content { get; set; } = string.Empty;

        // --- Navigation properties ---
        public ChatSession? Session { get; set; }          
    }
}
