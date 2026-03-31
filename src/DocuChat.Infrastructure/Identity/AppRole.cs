using Microsoft.AspNetCore.Identity;

namespace DocuChat.Infrastructure.Identity;

public class AppRole : IdentityRole
{
    public string? Description { get; set; }
}