using Microsoft.AspNetCore.Identity;

namespace DocuChat.Infrastructure.Persistence.Identity;

public class AppRole : IdentityRole
{
    public string? Description { get; set; }
}
