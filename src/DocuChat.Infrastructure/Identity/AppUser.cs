using Microsoft.AspNetCore.Identity;

namespace DocuChat.Infrastructure.Identity;

public class AppUser : IdentityUser
{
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}