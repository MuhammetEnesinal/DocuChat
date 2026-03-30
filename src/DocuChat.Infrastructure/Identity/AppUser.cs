using DocuChat.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocuChat.Infrastructure.Identity
{
    public class AppUser : IdentityUser
    {
        // --- Scalar properties ---
        public string? FullName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // --- Navigation properties ---
        public List<Document> Documents { get; set; } = new();     
        public List<ChatSession> Sessions { get; set; } = new();   
    }
}
