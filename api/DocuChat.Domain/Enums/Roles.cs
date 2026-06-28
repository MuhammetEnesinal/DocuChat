namespace DocuChat.Domain.Enums;

// Enum değil static class — [Authorize(Roles = "Admin")] string ister
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
