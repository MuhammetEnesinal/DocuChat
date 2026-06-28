using Microsoft.AspNetCore.Identity;

namespace DocuChat.Infrastructure.Persistence.Identity;

public class AppUser : IdentityUser
{
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
