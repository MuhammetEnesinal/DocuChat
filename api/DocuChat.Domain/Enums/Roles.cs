namespace DocuChat.Domain.Enums;

// [Authorize(Roles = "Admin")] string sabit beklediği için rol adları burada tutulur.
public static class Roles
{
    public const string Admin = "Admin";
    // Yönetici — admin'in kendisine atadığı departman(lar)a belge yükler/yönetir. Kullanıcı
    // yönetmez; yalnız atanan departmanların kapsamında iş yapar.
    public const string Manager = "Manager";
    public const string User = "User";
}
