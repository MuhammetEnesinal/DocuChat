namespace DocuChat.Infrastructure.Services.Auth;

// JWT'de kullanılan özel claim tipleri. Departman üyeliği token'a gömülür → her aramada
// DB'ye gitmeden okunur. Üyelik değişince kullanıcı yeniden login olmalı (kabul edilen kısıt).
public static class AppClaimTypes
{
    public const string Department = "dept";
}
